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
    private const string UnsupportedSignatureMessage = "Файл содержит неподдерживаемую сигнатуру или формат. Поддерживаются Автономный, LWD и Картограф с четырьмя передатчиками в режиме LWD 4tx с canonical GP_TOOL_DATA по 240 байт на кадр.";
    private const string InvalidSizeMessage = "Файл не содержит ни одного полного кадра. Ожидается не менее 240 байт (или 251 для .DEV).";

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
        int frameCount = checked((int)(fileSize / physicalFrameSize));
        if (frameCount == 0)
        {
            return DataFileValidationResult.Invalid(InvalidSizeMessage, format);
        }

        // A trailing partial frame is tolerated instead of failing the whole file.
        // A RAM dump taken off the tool stops wherever the recording stopped, so its
        // last record is routinely cut in the middle: the observed .DEV files end
        // with a fragment of the next 251-byte record. Legacy rejected such a file
        // outright and so did this reader, which left several thousand perfectly
        // readable frames unreachable. The fragment itself carries no complete
        // measurement and is skipped.
        int trailingByteCount = checked((int)(fileSize % physicalFrameSize));
        return new DataFileValidationResult(
            true,
            null,
            format,
            signature,
            physicalFrameSize,
            fileSize,
            frameCount,
            trailingByteCount);
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
        int skippedFrameCount = 0;
        for (int index = 0; index < validation.FrameCount; index++)
        {
            if (ReadParsedFrameAt(stream, validation.Format, validation.PhysicalFrameSize, index, rawFrame, out ParsedToolFrame frame))
            {
                records.Add(frame.CanonicalData);
                metadata.Add(new ToolRecordMetadata(frame.Kind, frame.Capabilities));
            }
            else
            {
                // A frame whose signature or layout does not parse is dropped, not
                // fatal: one damaged record in the middle of a long recording must
                // not make the rest of the file unreachable. The count is carried
                // out so the user can be told the file is not intact.
                skippedFrameCount++;
            }
        }

        if (records.Count == 0)
        {
            // The signature at the head of the file passed, so the file is of a
            // known tool; naming the frame count separates "wrong device" from
            // "frames do not match the canonical layout".
            throw new InvalidDataException(
                $"Ни один из {validation.FrameCount} кадров файла не удалось разобрать. "
                + $"Сигнатура файла — 0x{validation.Signature:X8}.");
        }

        return new DataLoadResult(
            records,
            metadata,
            BuildLoadedFileName(fileName),
            validation.Format,
            validation.PhysicalFrameSize,
            validation.TrailingByteCount,
            skippedFrameCount);
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
