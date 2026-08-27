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
    private const string UnsupportedSignatureMessage = "Файл содержит неподдерживаемую сигнатуру или формат. Поддерживаются Автономный, LWD и Картограф с четырьмя передатчиками в режиме LWD 4tx: кадр 240 байт у старой прошивки и 320 байт у новой.";
    private const string InvalidSizeMessage = "Файл не содержит ни одного полного кадра.";
    private const string AmbiguousLayoutMessage = "Размер кадра файла не удалось определить: сигнатура первого кадра не подтверждается ни на 240, ни на 320 байтах шага.";

    /// <summary>
    /// Сколько записей проверяется при подтверждении шага. Достаточно, чтобы
    /// отсечь случайное совпадение, и не заставляет читать весь файл.
    /// </summary>
    private const int LayoutProbeFrameCount = 4;

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

    /// <summary>
    /// Размер физической записи: кадр измерений плюс служебный заголовок
    /// `.DEV`. Размер самого кадра задаёт прибор, а не расширение файла.
    /// </summary>
    public static int GetPhysicalFrameSize(FileFormat format, int frameSize)
    {
        return format == FileFormat.Dev
            ? BinaryLayoutConstants.DevPhysicalFrameSize(frameSize)
            : frameSize;
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

        if (!TryReadFileSignature(fileName, format, out uint signature, out string? accessError))
        {
            // Отказ доступа и короткий файл — разные причины, и подменять одну
            // другой нельзя: пользователь получал «файл слишком короткий» на
            // совершенно исправном файле и искал повреждение там, где его нет.
            return DataFileValidationResult.Invalid(accessError ?? ShortFileMessage, format);
        }

        IToolDataAdapter? adapter = ToolDataAdapterRegistry.Default.FindAdapterBySignature(signature);
        if (adapter is null || !adapter.SupportsFileFormat(format))
        {
            return DataFileValidationResult.Invalid(UnsupportedSignatureMessage, format);
        }

        if (!TryResolveFrameSize(fileName, format, signature, fileSize, out int frameSize))
        {
            return DataFileValidationResult.Invalid(AmbiguousLayoutMessage, format);
        }

        int physicalFrameSize = GetPhysicalFrameSize(format, frameSize);
        int frameCount = checked((int)(fileSize / physicalFrameSize));
        if (frameCount == 0)
        {
            return DataFileValidationResult.Invalid(
                $"{InvalidSizeMessage} Ожидается не менее {physicalFrameSize} байт.",
                format);
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
            trailingByteCount,
            frameSize);
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

        using FileStream stream = OpenForSharedRead(fileName);
        byte[] rawFrame = new byte[validation.FrameSize];
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
            skippedFrameCount,
            validation.FrameSize);
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
        return TryReadFileSignature(fileName, format, out signature, out _);
    }

    private static bool TryReadFileSignature(
        string fileName,
        FileFormat format,
        out uint signature,
        out string? accessError)
    {
        signature = 0;
        accessError = null;
        int signatureOffset = GetSignatureOffset(format);
        byte[] buffer = new byte[sizeof(uint)];

        try
        {
            using FileStream stream = OpenForSharedRead(fileName);
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
            accessError = $"{OpenErrorMessage}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Открывает файл данных на чтение, не мешая тому, кто уже пишет в него.
    /// </summary>
    /// <remarks>
    /// <c>File.OpenRead</c> запрашивает <c>FileShare.Read</c>, а этот режим
    /// запрещает чужую запись. Файл, в который прямо сейчас идёт запись живых
    /// кадров, открыт на запись самим приложением, поэтому такое открытие
    /// срывается нарушением совместного доступа — и это штатный сценарий:
    /// пользователь открывает файл текущей сессии, чтобы посмотреть записанное.
    /// Тем же <c>FileShare.ReadWrite</c> уже читается файл при выгрузке в базу
    /// учёта приборов из-под активной записи.
    ///
    /// Незавершённая запись последнего кадра в открытом файле опасности не
    /// представляет: неполный хвост пропускается тем же правилом, что и у
    /// оборванного дампа памяти прибора.
    /// </remarks>
    private static FileStream OpenForSharedRead(string fileName)
    {
        return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>
    /// Определяет размер кадра в записях файла.
    /// </summary>
    /// <remarks>
    /// Первым кандидатом идёт размер из сигнатуры — это ответ прибора, и в
    /// подавляющем большинстве случаев он и подтверждается. Но сигнатуры
    /// старых приборов несут мусор в старших битах, а признак новой прошивки
    /// как раз в старших разрядах, поэтому одной сигнатуре доверять нельзя:
    /// выбранный размер обязан подтвердиться на самом файле — на шаге кандидата
    /// в начале каждой записи должна лежать та же сигнатура.
    ///
    /// Файл, состоящий ровно из одного кадра, подтвердить нечем; тогда
    /// работает размер из сигнатуры, если он совпадает с длиной файла.
    /// </remarks>
    private static bool TryResolveFrameSize(
        string fileName,
        FileFormat format,
        uint signature,
        long fileSize,
        out int frameSize)
    {
        frameSize = 0;
        int signatureFrameSize = ToolDataAdapterRegistry.DecodeFrameSize(signature);

        try
        {
            using FileStream stream = OpenForSharedRead(fileName);
            if (TryConfirmFrameSize(stream, format, signature, fileSize, signatureFrameSize))
            {
                frameSize = signatureFrameSize;
                return true;
            }

            foreach (int candidate in ToolSignature.KnownFrameSizes)
            {
                if (candidate != signatureFrameSize
                    && TryConfirmFrameSize(stream, format, signature, fileSize, candidate))
                {
                    frameSize = candidate;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool TryConfirmFrameSize(
        FileStream stream,
        FileFormat format,
        uint signature,
        long fileSize,
        int candidateFrameSize)
    {
        int physicalFrameSize = GetPhysicalFrameSize(format, candidateFrameSize);
        long availableFrames = fileSize / physicalFrameSize;
        if (availableFrames == 0)
        {
            return false;
        }

        if (availableFrames == 1)
        {
            // Подтверждать нечем: принимаем только размер, названный самим
            // прибором, и только если файл действительно содержит один кадр
            // такого размера.
            return candidateFrameSize == ToolDataAdapterRegistry.DecodeFrameSize(signature);
        }

        int signatureOffset = GetSignatureOffset(format);
        int probeCount = (int)Math.Min(availableFrames, LayoutProbeFrameCount);
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        for (int index = 1; index < probeCount; index++)
        {
            stream.Position = (index * (long)physicalFrameSize) + signatureOffset;
            if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) != buffer.Length
                || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != signature)
            {
                return false;
            }
        }

        return true;
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
