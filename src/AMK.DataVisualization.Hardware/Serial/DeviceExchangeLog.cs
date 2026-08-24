using System.Text;
using System.Threading.Channels;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed record DeviceLogLine(
    long Sequence,
    bool Bold,
    bool IsRawPayload,
    string Text);

public static class DeviceExchangeLog
{
    private const int MaximumBufferedLines = 300;
    private const int MaximumPendingFileCommands = 4096;
    private const int MaximumSessionFiles = 3;
    private const long MaximumLogFileBytes = 32L * 1024L * 1024L;
    private const string LogDirectoryName = "AMK-Data-Visualization";
    private const string LegacyLogFileName = "device_log.txt";
    private const string SessionLogFilePrefix = "device_log_";
    private const string FileLimitMessage =
        "[Журнал] Достигнут предел файла 32 МБ. Запись на диск приостановлена до следующей сессии.";

    private static readonly object Sync = new();
    private static readonly Queue<DeviceLogLine> Lines = new(MaximumBufferedLines);
    private static readonly Channel<FileCommand> FileCommands = Channel.CreateBounded<FileCommand>(
        new BoundedChannelOptions(MaximumPendingFileCommands)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private static readonly string ResolvedDirectoryPath = ResolveDirectoryPath();
    private static readonly Task FileWriterTask = Task.Run(ProcessFileCommandsAsync);

    private static long generation;
    private static long sequence;
    private static long sessionSequence;
    private static long droppedFileLines;
    private static bool enabled = true;
    private static bool showRawData;
    private static bool rawDataAvailable = true;
    private static DeviceLogSessionKind currentSessionKind;
    private static string currentFilePath = string.Empty;

    public static string DirectoryPath => ResolvedDirectoryPath;

    public static string FilePath
    {
        get
        {
            lock (Sync)
            {
                return currentFilePath;
            }
        }
    }

    public static int MaximumLines => MaximumBufferedLines;

    public static int MaximumFiles => MaximumSessionFiles;

    public static long Generation => Interlocked.Read(ref generation);

    public static bool IsEnabled
    {
        get
        {
            lock (Sync)
            {
                return enabled;
            }
        }
        set
        {
            lock (Sync)
            {
                if (enabled == value)
                {
                    return;
                }

                enabled = value;
                Interlocked.Increment(ref generation);
            }
        }
    }

    public static bool ShowRawData
    {
        get
        {
            lock (Sync)
            {
                return showRawData;
            }
        }
        set
        {
            lock (Sync)
            {
                bool normalized = rawDataAvailable && value;
                if (showRawData == normalized)
                {
                    return;
                }

                showRawData = normalized;
                Interlocked.Increment(ref generation);
            }
        }
    }

    public static bool IsRawDataAvailable
    {
        get
        {
            lock (Sync)
            {
                return rawDataAvailable;
            }
        }
    }

    public static int BufferedLineCount
    {
        get
        {
            lock (Sync)
            {
                return Lines.Count;
            }
        }
    }

    public static void SetRawDataAvailable(bool available)
    {
        lock (Sync)
        {
            bool changed = rawDataAvailable != available;
            rawDataAvailable = available;
            if (!available && showRawData)
            {
                showRawData = false;
                changed = true;
            }

            if (changed)
            {
                Interlocked.Increment(ref generation);
            }
        }
    }

    public static void BeginDeviceSession(string portName, int baudRate)
    {
        BeginSession(DeviceLogSessionKind.Device, $"{portName}_{baudRate}", reuseSameKind: false);
    }

    public static void BeginFileSession(string sourceFilePath)
    {
        string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
        BeginSession(DeviceLogSessionKind.File, sourceName, reuseSameKind: true);
    }

    public static void CloseFile()
    {
        lock (Sync)
        {
            currentSessionKind = DeviceLogSessionKind.None;
        }

        EnqueueControlCommand(new FileCommand(FileCommandKind.Close, string.Empty, string.Empty));
    }

    public static void Append(string line)
    {
        AppendCore(line, bold: false, isRawPayload: false);
    }

    public static void AppendRaw(string line)
    {
        AppendCore(line, bold: false, isRawPayload: true);
    }

    public static void AppendRawBold(string line)
    {
        AppendCore(line, bold: true, isRawPayload: true);
    }

    internal static void AppendFileOnly(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (Sync)
        {
            if (!enabled)
            {
                return;
            }
        }

        EnqueueFileLine(line);
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Lines.Clear();
            Interlocked.Increment(ref generation);
        }
    }

    public static IReadOnlyList<DeviceLogLine> GetLines()
    {
        lock (Sync)
        {
            return Lines
                .Where(line => showRawData || !line.IsRawPayload)
                .ToArray();
        }
    }

    public static string GetFullText()
    {
        lock (Sync)
        {
            return string.Join(
                Environment.NewLine,
                Lines.Where(line => showRawData || !line.IsRawPayload).Select(line => line.Text));
        }
    }

    public static void AppendDeviceIdentity(
        uint signature,
        uint serial,
        uint rxPosition = 0,
        uint sondeDiameterMillimeters = 0)
    {
        if (!IsEnabled)
        {
            return;
        }

        // Всё, что кодирует сигнатура, разбирает единственный декодер
        // ToolSignature. Раньше здесь дублировалось маскирование младших
        // 20 бит, и на сигнатуре новой прошивки, где размер кадра лежит в
        // старших разрядах, это давало чужие число передатчиков, модификацию
        // и номер.
        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(signature);
        string firmware = identity.IsExtendedFirmware ? "новая" : "старая";
        string receiverPosition = rxPosition == 1 ? "R1→T2" : "R1→T1";

        List<string> identityLines =
        [
            "========== Идентификация зонда ==========",
            $"Тип: {identity.DisplayName}",
            $"N Tx: {identity.TransmitterCount}",
            $"Мод: {identity.Modification}",
            // Подписи совпадают с окном метрологии: значение из сигнатуры —
            // серийный номер прибора, поле Serial структуры — номер корпуса.
            $"Серийный №: {identity.Number}",
            $"Корпус \u2116: {serial}",
            $"Позиция R1: {receiverPosition}",
            $"Прошивка: {firmware}, кадр {identity.FrameSize} Б",
            $"Сигнатура: 0x{signature:X8}"
        ];
        if (sondeDiameterMillimeters > 0)
        {
            identityLines.Add($"Диаметр прибора: {sondeDiameterMillimeters} мм");
        }

        identityLines.Add("========================================");
        foreach (string line in identityLines)
        {
            AppendCore(line, bold: true, isRawPayload: false);
        }
    }

    private static void BeginSession(DeviceLogSessionKind kind, string source, bool reuseSameKind)
    {
        string sessionFilePath;
        lock (Sync)
        {
            if (reuseSameKind && currentSessionKind == kind && currentFilePath.Length > 0)
            {
                return;
            }

            currentSessionKind = kind;
            sessionFilePath = BuildSessionFilePath(kind, source);
            currentFilePath = sessionFilePath;
        }

        EnqueueControlCommand(new FileCommand(FileCommandKind.Open, sessionFilePath, string.Empty));
    }

    private static string BuildSessionFilePath(DeviceLogSessionKind kind, string source)
    {
        long currentSession = Interlocked.Increment(ref sessionSequence);
        string kindLabel = kind == DeviceLogSessionKind.Device ? "COM" : "FILE";
        string safeSource = SanitizeFileNamePart(source);
        string sourceSuffix = safeSource.Length > 0 ? $"_{safeSource}" : string.Empty;
        string fileName =
            $"{SessionLogFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}_{kindLabel}{sourceSuffix}_S{currentSession:D4}.txt";
        return Path.Combine(ResolvedDirectoryPath, fileName);
    }

    private static string SanitizeFileNamePart(string value)
    {
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        StringBuilder builder = new(Math.Min(value.Length, 40));
        foreach (char character in value)
        {
            if (builder.Length >= 40)
            {
                break;
            }

            if (!invalidCharacters.Contains(character) && !char.IsControl(character))
            {
                builder.Append(character == ' ' ? '_' : character);
            }
        }

        return builder.ToString().Trim('_', '.');
    }

    private static void AppendCore(string line, bool bold, bool isRawPayload)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (Sync)
        {
            if (!enabled)
            {
                return;
            }

            long currentSequence = Interlocked.Increment(ref sequence);
            Lines.Enqueue(new DeviceLogLine(currentSequence, bold, isRawPayload, line));
            while (Lines.Count > MaximumBufferedLines)
            {
                Lines.Dequeue();
            }

            Interlocked.Increment(ref generation);
        }

        EnqueueFileLine(line);
    }

    private static void EnqueueFileLine(string line)
    {
        if (!FileCommands.Writer.TryWrite(new FileCommand(FileCommandKind.Write, string.Empty, line)))
        {
            Interlocked.Increment(ref droppedFileLines);
        }
    }

    private static void EnqueueControlCommand(FileCommand command)
    {
        try
        {
            FileCommands.Writer.WriteAsync(command).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Журналирование не должно прерывать обмен с физическим прибором.
        }
    }

    private static async Task ProcessFileCommandsAsync()
    {
        StreamWriter? writer = null;
        long bytesWritten = 0;
        bool limitReported = false;
        try
        {
            while (await FileCommands.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (FileCommands.Reader.TryRead(out FileCommand command))
                {
                    try
                    {
                        switch (command.Kind)
                        {
                            case FileCommandKind.Open:
                                await DisposeWriterAsync(writer).ConfigureAwait(false);
                                writer = null;
                                bytesWritten = 0;
                                limitReported = false;
                                Interlocked.Exchange(ref droppedFileLines, 0);
                                Directory.CreateDirectory(ResolvedDirectoryPath);

                                FileStream stream = new(
                                    command.FilePath,
                                    FileMode.Create,
                                    FileAccess.Write,
                                    FileShare.ReadWrite,
                                    bufferSize: 64 * 1024,
                                    useAsync: true);
                                writer = new StreamWriter(
                                    stream,
                                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                    64 * 1024)
                                {
                                    AutoFlush = false
                                };
                                PruneSessionFiles(command.FilePath);
                                break;

                            case FileCommandKind.Close:
                                await DisposeWriterAsync(writer).ConfigureAwait(false);
                                writer = null;
                                break;

                            case FileCommandKind.Write when writer is not null:
                                long dropped = Interlocked.Exchange(ref droppedFileLines, 0);
                                if (dropped > 0)
                                {
                                    string dropMessage =
                                        $"[Журнал] Пропущено строк при перегрузке файловой очереди: {dropped}.";
                                    bytesWritten = await TryWriteLineAsync(
                                            writer,
                                            dropMessage,
                                            bytesWritten)
                                        .ConfigureAwait(false);
                                }

                                long required = Encoding.UTF8.GetByteCount(command.Text) + Environment.NewLine.Length;
                                if (bytesWritten + required <= MaximumLogFileBytes)
                                {
                                    await writer.WriteLineAsync(command.Text).ConfigureAwait(false);
                                    bytesWritten += required;
                                }
                                else if (!limitReported)
                                {
                                    await writer.WriteLineAsync(FileLimitMessage).ConfigureAwait(false);
                                    limitReported = true;
                                }
                                break;
                        }
                    }
                    catch
                    {
                        await DisposeWriterAsync(writer).ConfigureAwait(false);
                        writer = null;
                    }
                }

                if (writer is not null)
                {
                    try
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        await DisposeWriterAsync(writer).ConfigureAwait(false);
                        writer = null;
                    }
                }
            }
        }
        catch
        {
            // Журналирование не должно прерывать обмен с физическим прибором.
        }
        finally
        {
            await DisposeWriterAsync(writer).ConfigureAwait(false);
        }
    }

    private static void PruneSessionFiles(string activeFilePath)
    {
        try
        {
            List<FileInfo> files = Directory
                .EnumerateFiles(ResolvedDirectoryPath, "device_log*.txt", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file =>
                    file.Name.Equals(LegacyLogFileName, StringComparison.OrdinalIgnoreCase)
                    || file.Name.StartsWith(SessionLogFilePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            while (files.Count > MaximumSessionFiles)
            {
                FileInfo? oldest = files.FirstOrDefault(file =>
                    !file.FullName.Equals(activeFilePath, StringComparison.OrdinalIgnoreCase));
                if (oldest is null)
                {
                    break;
                }

                files.Remove(oldest);
                try
                {
                    oldest.Delete();
                }
                catch
                {
                    // Повторная сессия снова попытается привести каталог к лимиту.
                }
            }
        }
        catch
        {
            // Ошибка ротации не должна останавливать текущую запись.
        }
    }

    private static async Task<long> TryWriteLineAsync(StreamWriter writer, string text, long bytesWritten)
    {
        long required = Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length;
        if (bytesWritten + required > MaximumLogFileBytes)
        {
            return bytesWritten;
        }

        await writer.WriteLineAsync(text).ConfigureAwait(false);
        return bytesWritten + required;
    }

    private static async ValueTask DisposeWriterAsync(StreamWriter? writer)
    {
        if (writer is null)
        {
            return;
        }

        try
        {
            await writer.FlushAsync().ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string ResolveDirectoryPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, LogDirectoryName, "Logs");
    }

    private enum DeviceLogSessionKind
    {
        None,
        Device,
        File
    }

    private enum FileCommandKind
    {
        Open,
        Write,
        Close
    }

    private readonly record struct FileCommand(FileCommandKind Kind, string FilePath, string Text);
}
