using System.Buffers.Binary;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.IO.DataFiles;

public static class ToolDataFileReader
{
    private const string UnsupportedFormatMessage = "Неподдерживаемый формат файла";
    private const string OpenErrorMessage = "Не удалось открыть файл данных";
    private const string ShortFileMessage = "Файл слишком короткий или не содержит корректную сигнатуру";
    private const string UnsupportedSignatureMessage = "Файл содержит неподдерживаемую сигнатуру или формат. Поддерживаются приборы с canonical GP_TOOL_DATA (240 байт на кадр).";
    private const string InvalidSizeMessage = "Файл имеет неправильный размер или повреждён. Ожидается кратность 240 (или 251 для .DEV) байт.";

    public static FileFormat DetectFileFormat(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".bin" => FileFormat.Bin,
            ".biv" => FileFormat.Biv,
            ".dev" => FileFormat.Dev,
            _ => FileFormat.Unknown
        };
    }

    public static int GetPhysicalFrameSize(FileFormat format)
    {
        return format == FileFormat.Dev
            ? BinaryLayoutConstants.DevPhysicalFrameSize
            : BinaryLayoutConstants.GpToolDataSize;
    }

    public static int GetSignatureOffset(FileFormat format)
    {
        return format == FileFormat.Dev ? BinaryLayoutConstants.DevHeaderSize : 0;
    }

    public static DataFileValidationResult ValidateDataFile(string fileName)
    {
        FileFormat format = DetectFileFormat(fileName);
        if (format == FileFormat.Unknown)
        {
            return DataFileValidationResult.Invalid(UnsupportedFormatMessage, format);
        }

        FileInfo fileInfo = new(fileName);
        if (!fileInfo.Exists)
        {
            return DataFileValidationResult.Invalid(OpenErrorMessage, format);
        }

        long fileSize = fileInfo.Length;
        if (fileSize <= 0)
        {
            return DataFileValidationResult.Invalid(ShortFileMessage, format);
        }

        if (!TryReadFileSignature(fileName, format, out uint signature))
        {
            return DataFileValidationResult.Invalid(ShortFileMessage, format);
        }

        IToolDataAdapter? adapter = ToolDataAdapterRegistry.Default.FindAdapterBySignature(signature);
        if (adapter is null || !adapter.SupportsFileFormat(format))
        {
            return DataFileValidationResult.Invalid(UnsupportedSignatureMessage, format);
        }

        int physicalFrameSize = GetPhysicalFrameSize(format);
        if (fileSize < physicalFrameSize || fileSize % physicalFrameSize != 0)
        {
            return DataFileValidationResult.Invalid(InvalidSizeMessage, format);
        }

        int frameCount = checked((int)(fileSize / physicalFrameSize));
        return new DataFileValidationResult(true, null, format, signature, physicalFrameSize, fileSize, frameCount);
    }

    public static bool TryValidateDataFile(string fileName, out DataFileValidationResult result)
    {
        result = ValidateDataFile(fileName);
        return result.IsValid;
    }

    public static DataLoadResult LoadDataFromFile(string fileName)
    {
        DataFileValidationResult validation = ValidateDataFile(fileName);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(validation.ErrorMessage ?? "Файл данных не прошёл проверку.");
        }

        List<GpToolData> records = new(validation.FrameCount);
        List<ToolRecordMetadata> metadata = new(validation.FrameCount);

        using FileStream stream = File.OpenRead(fileName);
        byte[] rawFrame = new byte[BinaryLayoutConstants.GpToolDataSize];
        for (int index = 0; index < validation.FrameCount; index++)
        {
            if (ReadParsedFrameAt(stream, validation.Format, validation.PhysicalFrameSize, index, rawFrame, out ParsedToolFrame frame))
            {
                records.Add(frame.CanonicalData);
                metadata.Add(new ToolRecordMetadata(frame.Kind, frame.Capabilities));
            }
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("Не удалось прочитать данные из файла.");
        }

        return new DataLoadResult(records, metadata, BuildLoadedFileName(fileName), validation.Format, validation.PhysicalFrameSize);
    }

    public static bool TryLoadDataFromFile(string fileName, out DataLoadResult? result, out string? errorMessage)
    {
        try
        {
            result = LoadDataFromFile(fileName);
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            result = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryReadFileSignature(string fileName, FileFormat format, out uint signature)
    {
        signature = 0;
        int signatureOffset = GetSignatureOffset(format);
        byte[] buffer = new byte[sizeof(uint)];

        try
        {
            using FileStream stream = File.OpenRead(fileName);
            if (stream.Length < signatureOffset + buffer.Length)
            {
                return false;
            }

            stream.Position = signatureOffset;
            return stream.Read(buffer, 0, buffer.Length) == buffer.Length
                && TryReadUInt32LittleEndian(buffer, out signature);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ReadParsedFrameAt(
        FileStream stream,
        FileFormat format,
        int physicalFrameSize,
        int frameIndex,
        byte[] rawFrame,
        out ParsedToolFrame frame)
    {
        frame = default;
        long frameOffset = checked((long)frameIndex * physicalFrameSize);
        if (format == FileFormat.Dev)
        {
            frameOffset += BinaryLayoutConstants.DevHeaderSize;
        }

        stream.Position = frameOffset;
        return stream.Read(rawFrame, 0, rawFrame.Length) == rawFrame.Length
            && ToolDataAdapterRegistry.Default.TryParseRawFrame(rawFrame, out frame);
    }

    private static bool TryReadUInt32LittleEndian(ReadOnlySpan<byte> buffer, out uint value)
    {
        value = 0;
        if (buffer.Length < sizeof(uint))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return true;
    }

    private static string BuildLoadedFileName(string fileName)
    {
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
