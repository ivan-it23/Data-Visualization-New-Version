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
    private const long MaximumLogFileBytes = 32L * 1024L * 1024L;
    private const string LogDirectoryName = "AMK-Data-Visualization";
    private const string LogFileName = "device_log.txt";
    private const string FileLimitMessage =
        "[Журнал] Достигнут предел файла 32 МБ. Запись на диск приостановлена до следующего подключения.";

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
    private static readonly string ResolvedFilePath = ResolveFilePath();
    private static readonly Task FileWriterTask = Task.Run(ProcessFileCommandsAsync);

    private static long generation;
    private static long sequence;
    private static long droppedFileLines;
    private static bool enabled = true;
    private static bool showRawData;
    private static bool rawDataAvailable = true;

    public static string FilePath => ResolvedFilePath;

    public static int MaximumLines => MaximumBufferedLines;

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

    public static void OpenFile()
    {
        EnqueueControlCommand(new FileCommand(FileCommandKind.Open, string.Empty));
    }

    public static void CloseFile()
    {
        EnqueueControlCommand(new FileCommand(FileCommandKind.Close, string.Empty));
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
        int modification = -1,
        int number = -1,
        uint rxPosition = 0,
        uint sondeDiameterMillimeters = 0)
    {
        if (!IsEnabled)
        {
            return;
        }

        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(signature);
        uint low20 = (signature << 12) >> 12;
        int decodedTransmitterCount = (int)((low20 % 100000u) / 10000u);
        int transmitterCount = decodedTransmitterCount > 0
            ? decodedTransmitterCount
            : identity.TransmitterCount;
        int finalModification = modification >= 0
            ? modification
            : (int)((low20 % 10000u) / 1000u);
        int finalNumber = number >= 0 ? number : (int)(low20 % 1000u);
        string receiverPosition = rxPosition == 1 ? "R1→T2" : "R1→T1";

        List<string> identityLines =
        [
            "========== Идентификация зонда ==========",
            $"Тип: {identity.DisplayName}",
            $"N Tx: {transmitterCount}",
            $"Мод: {finalModification}",
            $"Номер: {finalNumber}",
            $"Серийный №: {serial}",
            $"Позиция R1: {receiverPosition}"
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

        if (!FileCommands.Writer.TryWrite(new FileCommand(FileCommandKind.Write, line)))
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
                                string? directory = Path.GetDirectoryName(ResolvedFilePath);
                                if (!string.IsNullOrWhiteSpace(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                FileStream stream = new(
                                    ResolvedFilePath,
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

    private static string ResolveFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, LogDirectoryName, "Logs", LogFileName);
    }

    private enum FileCommandKind
    {
        Open,
        Write,
        Close
    }

    private readonly record struct FileCommand(FileCommandKind Kind, string Text);
}
