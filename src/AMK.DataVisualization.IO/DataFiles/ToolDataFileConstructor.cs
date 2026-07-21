using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.IO.DataFiles;

public sealed record FileSegmentLayout(
    int StartIndex,
    int RecordCount,
    int SourceStartIndex,
    string SourcePath,
    string Label);

public sealed class FileConstructorSegment
{
    internal FileConstructorSegment(
        string sourcePath,
        string label,
        int sourceStartIndex,
        IReadOnlyList<GpToolData> records)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        Label = label;
        SourceStartIndex = sourceStartIndex;
        Records = records;
        IsHomogeneous = ToolDataFileConstructor.TryGetHomogeneousSignature(records, out uint signature);
        Signature = signature;
    }

    public string SourcePath { get; }

    public string Label { get; }

    public int SourceStartIndex { get; }

    public IReadOnlyList<GpToolData> Records { get; }

    public int RecordCount => Records.Count;

    public uint Signature { get; }

    public bool IsHomogeneous { get; }
}

public sealed record FileConstructorSaveResult(bool DataSaved, bool LayoutSaved, string? Warning)
{
    public bool Succeeded => DataSaved;
}

public static class ToolDataFileConstructor
{
    private const string SidecarMagic = "AMK_HORIZON_SEGMENTS_V1";
    private const string ApplicationFolderName = "AMK-Data-Visualization";
    private const string LegacyApplicationFolderName = "AMK-Horizon-New-Chart";

    public static FileConstructorSegment LoadSegment(
        string sourcePath,
        int sourceStartIndex = 0,
        int? recordCount = null,
        string? label = null)
    {
        DataLoadResult loaded = ToolDataFileReader.LoadDataFromFile(sourcePath);
        int count = recordCount ?? (loaded.Records.Count - sourceStartIndex);
        if (sourceStartIndex < 0
            || count <= 0
            || sourceStartIndex > loaded.Records.Count
            || count > loaded.Records.Count - sourceStartIndex)
        {
            throw new InvalidDataException("Описание сборки ссылается на отсутствующий диапазон исходного файла.");
        }

        GpToolData[] records = loaded.Records
            .Skip(sourceStartIndex)
            .Take(count)
            .ToArray();
        return new FileConstructorSegment(
            sourcePath,
            string.IsNullOrWhiteSpace(label) ? Path.GetFileName(sourcePath) : label,
            sourceStartIndex,
            records);
    }

    public static FileConstructorSegment CreateSegment(
        string sourcePath,
        int sourceStartIndex,
        IEnumerable<GpToolData> records,
        string label)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Для добавления сегмента требуется существующий исходный файл данных.",
                sourcePath);
        }

        GpToolData[] copiedRecords = records.ToArray();
        if (copiedRecords.Length == 0)
        {
            throw new InvalidDataException("Сегмент не содержит кадров.");
        }

        return new FileConstructorSegment(sourcePath, label, sourceStartIndex, copiedRecords);
    }

    public static IReadOnlyList<FileConstructorSegment> LoadAssembly(string dataFilePath)
    {
        if (!TryLoadSegmentLayout(dataFilePath, out IReadOnlyList<FileSegmentLayout> layout) || layout.Count == 0)
        {
            throw new InvalidDataException(
                "Для выбранного файла не найдено описание сборки. Изменение порядка доступно только для файлов, созданных конструктором или через добавление отрезков.");
        }

        List<FileConstructorSegment> segments = new(layout.Count);
        foreach (FileSegmentLayout item in layout)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                throw new FileNotFoundException("Не найден исходный файл сегмента.", item.SourcePath);
            }

            segments.Add(LoadSegment(
                item.SourcePath,
                item.SourceStartIndex,
                item.RecordCount,
                item.Label));
        }

        return segments;
    }

    public static bool AreCompatible(IReadOnlyList<FileConstructorSegment> segments, out uint signature)
    {
        signature = 0;
        if (segments.Count == 0 || segments.Any(segment => !segment.IsHomogeneous))
        {
            return false;
        }

        signature = segments[0].Signature;
        uint expectedSignature = signature;
        return segments.All(segment => segment.Signature == expectedSignature);
    }

    public static FileConstructorSaveResult SaveConstructedFile(
        string outputPath,
        IReadOnlyList<FileConstructorSegment> segments)
    {
        if (!AreCompatible(segments, out uint signature))
        {
            throw new InvalidDataException(
                "Нельзя сохранить единый файл: все сегменты должны иметь одинаковую сигнатуру одного прибора.");
        }

        EnsureCanonicalBinPath(outputPath);
        List<GpToolData> combinedRecords = new(segments.Sum(segment => segment.RecordCount));
        List<FileSegmentLayout> layout = new(segments.Count);
        int startIndex = 0;
        foreach (FileConstructorSegment segment in segments)
        {
            layout.Add(new FileSegmentLayout(
                startIndex,
                segment.RecordCount,
                segment.SourceStartIndex,
                segment.SourcePath,
                segment.Label));
            combinedRecords.AddRange(segment.Records);
            startIndex += segment.RecordCount;
        }

        WriteCanonicalFileAtomically(outputPath, combinedRecords);
        try
        {
            SaveSegmentLayout(outputPath, signature, layout);
            return new FileConstructorSaveResult(true, true, null);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            DeleteSegmentLayout(outputPath);
            return new FileConstructorSaveResult(
                true,
                false,
                $"Файл сохранён, но не удалось сохранить описание границ сегментов: {exception.Message}");
        }
    }

    public static FileConstructorSaveResult AppendSegmentsToExistingFile(
        string targetFilePath,
        IReadOnlyList<FileConstructorSegment> appendedSegments)
    {
        EnsureCanonicalBinPath(targetFilePath);
        if (!AreCompatible(appendedSegments, out uint appendedSignature))
        {
            throw new InvalidDataException("Добавляемые данные содержат разные сигнатуры прибора.");
        }

        DataFileValidationResult validation = ToolDataFileReader.ValidateDataFile(targetFilePath);
        if (!validation.IsValid || validation.Format != FileFormat.Bin
            || validation.PhysicalFrameSize != BinaryLayoutConstants.GpToolDataSize)
        {
            throw new InvalidDataException(
                "Добавление поддерживается только для канонических .bin файлов по 240 байт на кадр.");
        }

        DataLoadResult targetData = ToolDataFileReader.LoadDataFromFile(targetFilePath);
        if (!TryGetHomogeneousSignature(targetData.Records, out uint targetSignature))
        {
            throw new InvalidDataException("Целевой файл содержит разные сигнатуры прибора.");
        }

        if (targetSignature != appendedSignature)
        {
            throw new InvalidDataException("Сигнатура добавляемых данных не совпадает с сигнатурой целевого файла.");
        }

        List<GpToolData> combinedRecords = new(targetData.Records.Count + appendedSegments.Sum(segment => segment.RecordCount));
        combinedRecords.AddRange(targetData.Records);
        foreach (FileConstructorSegment segment in appendedSegments)
        {
            combinedRecords.AddRange(segment.Records);
        }

        List<FileSegmentLayout> layout;
        if (!TryLoadSegmentLayout(targetFilePath, out IReadOnlyList<FileSegmentLayout> existingLayout)
            || existingLayout.Sum(segment => segment.RecordCount) != targetData.Records.Count)
        {
            layout =
            [
                new FileSegmentLayout(
                    0,
                    targetData.Records.Count,
                    0,
                    Path.GetFullPath(targetFilePath),
                    Path.GetFileName(targetFilePath))
            ];
        }
        else
        {
            layout = existingLayout.ToList();
        }

        int startIndex = targetData.Records.Count;
        foreach (FileConstructorSegment segment in appendedSegments)
        {
            layout.Add(new FileSegmentLayout(
                startIndex,
                segment.RecordCount,
                segment.SourceStartIndex,
                segment.SourcePath,
                segment.Label));
            startIndex += segment.RecordCount;
        }

        WriteCanonicalFileAtomically(targetFilePath, combinedRecords);
        try
        {
            SaveSegmentLayout(targetFilePath, targetSignature, layout);
            return new FileConstructorSaveResult(true, true, null);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            DeleteSegmentLayout(targetFilePath);
            return new FileConstructorSaveResult(
                true,
                false,
                $"Данные добавлены, но не удалось сохранить описание границ сегментов: {exception.Message}");
        }
    }

    public static bool TryLoadSegmentLayout(string dataFilePath, out IReadOnlyList<FileSegmentLayout> segments)
    {
        segments = [];
        string primaryPath = BuildSegmentLayoutPath(dataFilePath);
        foreach (string candidate in GetSegmentLayoutCandidates(dataFilePath))
        {
            if (!TryReadSegmentLayout(candidate, out List<FileSegmentLayout> loaded))
            {
                continue;
            }

            segments = loaded;
            if (!PathsEqual(candidate, primaryPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
                    File.Copy(candidate, primaryPath, true);
                }
                catch (Exception exception) when (IsFileOperationException(exception))
                {
                }
            }

            return true;
        }

        return false;
    }

    public static bool SaveLayoutExcludingRange(
        string sourceDataFilePath,
        string targetDataFilePath,
        int startIndex,
        int endIndex,
        int originalRecordCount,
        out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            if (!TryLoadSegmentLayout(sourceDataFilePath, out IReadOnlyList<FileSegmentLayout> sourceSegments))
            {
                DeleteSegmentLayout(targetDataFilePath);
                return true;
            }

            if (startIndex < 0 || startIndex > endIndex || endIndex >= originalRecordCount)
            {
                errorMessage = "Некорректный диапазон для обновления границ сегментов.";
                return false;
            }

            int cutEndExclusive = endIndex + 1;
            int newStart = 0;
            List<FileSegmentLayout> updated = [];
            foreach (FileSegmentLayout segment in sourceSegments)
            {
                int segmentStart = segment.StartIndex;
                int segmentEnd = segment.StartIndex + segment.RecordCount;
                AppendKeptPiece(segment, segmentStart, Math.Min(segmentEnd, startIndex), ref newStart, updated);
                AppendKeptPiece(segment, Math.Max(segmentStart, cutEndExclusive), segmentEnd, ref newStart, updated);
            }

            if (updated.Count == 0)
            {
                DeleteSegmentLayout(targetDataFilePath);
                return true;
            }

            DataLoadResult targetData = ToolDataFileReader.LoadDataFromFile(targetDataFilePath);
            if (!TryGetHomogeneousSignature(targetData.Records, out uint signature))
            {
                errorMessage = "Файл после вырезания содержит разные сигнатуры прибора.";
                return false;
            }

            SaveSegmentLayout(targetDataFilePath, signature, updated);
            return true;
        }
        catch (Exception exception) when (IsFileOperationException(exception) || exception is InvalidDataException)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    public static void DeleteSegmentLayout(string dataFilePath)
    {
        foreach (string path in GetSegmentLayoutCandidates(dataFilePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (IsFileOperationException(exception))
            {
            }
        }
    }

    public static string BuildSegmentLayoutPath(string dataFilePath)
    {
        return BuildSegmentLayoutPath(dataFilePath, ApplicationFolderName);
    }

    internal static bool TryGetHomogeneousSignature(IEnumerable<GpToolData> records, out uint signature)
    {
        signature = 0;
        using IEnumerator<GpToolData> enumerator = records.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        signature = enumerator.Current.Signature;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Signature != signature)
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendKeptPiece(
        FileSegmentLayout segment,
        int pieceStart,
        int pieceEnd,
        ref int newStart,
        List<FileSegmentLayout> target)
    {
        int segmentStart = segment.StartIndex;
        int segmentEnd = segment.StartIndex + segment.RecordCount;
        pieceStart = Math.Clamp(pieceStart, segmentStart, segmentEnd);
        pieceEnd = Math.Clamp(pieceEnd, segmentStart, segmentEnd);
        if (pieceEnd <= pieceStart)
        {
            return;
        }

        int count = pieceEnd - pieceStart;
        target.Add(segment with
        {
            StartIndex = newStart,
            RecordCount = count,
            SourceStartIndex = segment.SourceStartIndex + (pieceStart - segment.StartIndex)
        });
        newStart += count;
    }

    private static void EnsureCanonicalBinPath(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Результат конструктора и целевой файл добавления должны иметь формат .bin.");
        }
    }

    private static void WriteCanonicalFileAtomically(string targetPath, IReadOnlyList<GpToolData> records)
    {
        if (records.Count == 0)
        {
            throw new InvalidDataException("Нет данных для сохранения.");
        }

        string fullTargetPath = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new DirectoryNotFoundException("Не удалось определить каталог целевого файла.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[BinaryLayoutConstants.GpToolDataSize];
                foreach (GpToolData record in records)
                {
                    MemoryMarshal.Write(buffer.AsSpan(), in record);
                    stream.Write(buffer);
                }

                stream.Flush(true);
            }

            File.Move(temporaryPath, fullTargetPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void SaveSegmentLayout(
        string dataFilePath,
        uint signature,
        IReadOnlyList<FileSegmentLayout> segments)
    {
        string path = BuildSegmentLayoutPath(dataFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            using (StreamWriter writer = new(temporaryPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(SidecarMagic);
                writer.WriteLine($"signature\t{signature.ToString(CultureInfo.InvariantCulture)}");
                foreach (FileSegmentLayout segment in segments)
                {
                    writer.WriteLine(string.Join('\t',
                        "segment",
                        segment.StartIndex.ToString(CultureInfo.InvariantCulture),
                        segment.RecordCount.ToString(CultureInfo.InvariantCulture),
                        segment.SourceStartIndex.ToString(CultureInfo.InvariantCulture),
                        EscapeField(segment.SourcePath),
                        EscapeField(segment.Label)));
                }
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryReadSegmentLayout(string path, out List<FileSegmentLayout> segments)
    {
        segments = [];
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (!string.Equals(reader.ReadLine(), SidecarMagic, StringComparison.Ordinal))
            {
                return false;
            }

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] parts = line.Split('\t');
                if (parts.Length == 0 || !string.Equals(parts[0], "segment", StringComparison.Ordinal))
                {
                    continue;
                }

                if (parts.Length < 6
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int startIndex)
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int recordCount)
                    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int sourceStartIndex)
                    || startIndex < 0
                    || recordCount <= 0
                    || sourceStartIndex < 0)
                {
                    segments = [];
                    return false;
                }

                segments.Add(new FileSegmentLayout(
                    startIndex,
                    recordCount,
                    sourceStartIndex,
                    UnescapeField(parts[4]),
                    UnescapeField(parts[5])));
            }

            return segments.Count > 0;
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            segments = [];
            return false;
        }
    }

    private static IEnumerable<string> GetSegmentLayoutCandidates(string dataFilePath)
    {
        yield return BuildSegmentLayoutPath(dataFilePath, ApplicationFolderName);
        yield return BuildSegmentLayoutPath(dataFilePath, LegacyApplicationFolderName);
        yield return Path.GetFullPath(dataFilePath) + ".segments.tsv";
    }

    private static string BuildSegmentLayoutPath(string dataFilePath, string applicationFolderName)
    {
        string fullPath = Path.GetFullPath(dataFilePath);
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            applicationFolderName,
            "segments");
        return Path.Combine(root, $"{Path.GetFileName(fullPath)}.{HashNormalizedPath(fullPath):X16}.segments.tsv");
    }

    private static ulong HashNormalizedPath(string path)
    {
        string normalized = Path.GetFullPath(path).ToLowerInvariant();
        ulong hash = 14695981039346656037UL;
        unchecked
        {
            foreach (char character in normalized)
            {
                hash ^= (byte)(character & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }

    private static string EscapeField(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string UnescapeField(string value)
    {
        StringBuilder result = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length)
            {
                result.Append(value[index]);
                continue;
            }

            char escaped = value[++index];
            result.Append(escaped switch
            {
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                '\\' => '\\',
                _ => escaped
            });
        }

        return result.ToString();
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileOperationException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or NotSupportedException;
    }
}
