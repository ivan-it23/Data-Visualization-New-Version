using System.Runtime.InteropServices;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.IO.DataFiles;

public static class ToolDataRangeExporter
{
    public static bool SaveDataRange(ToolDataRepository repository, string fileName, int startIndex, int endIndex)
    {
        if (!repository.HasData)
        {
            return false;
        }

        if (startIndex < 0 || startIndex >= repository.DataSize || endIndex >= repository.DataSize || startIndex > endIndex)
        {
            return false;
        }

        try
        {
            using FileStream stream = File.Create(fileName);
            return WriteCanonicalDataRange(stream, repository, startIndex, endIndex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool SaveFileExcludingRange(string sourceFileName, string targetFileName, int startIndex, int endIndex)
    {
        if (string.IsNullOrEmpty(sourceFileName)
            || string.IsNullOrEmpty(targetFileName)
            || string.Equals(sourceFileName, targetFileName, StringComparison.Ordinal))
        {
            return false;
        }

        FileFormat sourceFormat = ToolDataFileReader.DetectFileFormat(sourceFileName);
        FileFormat targetFormat = ToolDataFileReader.DetectFileFormat(targetFileName);
        if (!IsSupportedDataFileFormat(sourceFormat) || sourceFormat != targetFormat)
        {
            return false;
        }

        try
        {
            using FileStream sourceFile = File.OpenRead(sourceFileName);
            long fileSize = sourceFile.Length;
            if (fileSize <= 0)
            {
                return false;
            }

            // Шаг записи берётся из проверки самого файла: у нового прибора
            // кадр 320 байт, и посчитанный по константе шаг разрезал бы файл
            // не по границам записей.
            DataFileValidationResult validation = ToolDataFileReader.ValidateDataFile(sourceFileName);
            if (!validation.IsValid)
            {
                return false;
            }

            int physicalFrameSize = validation.PhysicalFrameSize;

            // Only complete frames are copied. A source whose recording stopped
            // mid-record is accepted here for the same reason the reader accepts it,
            // and its trailing fragment is left out of the result rather than
            // rejecting the operation.
            long frameCount = fileSize / physicalFrameSize;
            if (frameCount == 0
                || startIndex < 0
                || endIndex < 0
                || startIndex >= frameCount
                || endIndex >= frameCount
                || startIndex > endIndex
                || (startIndex == 0 && endIndex == frameCount - 1))
            {
                return false;
            }

            using FileStream targetFile = File.Create(targetFileName);
            byte[] frameBuffer = new byte[physicalFrameSize];
            for (long index = 0; index < frameCount; index++)
            {
                if (!ReadExactly(sourceFile, frameBuffer))
                {
                    return false;
                }

                if (index >= startIndex && index <= endIndex)
                {
                    continue;
                }

                targetFile.Write(frameBuffer, 0, frameBuffer.Length);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool WriteCanonicalDataRange(FileStream stream, ToolDataRepository repository, int startIndex, int endIndex)
    {
        // Кадр в памяти всегда 320 байт, но на диск уходит ровно столько, сколько
        // отдал прибор: у источника старой прошивки хвост canonical-структуры
        // заполнен нулями приложением, и записывать его означало бы выдавать
        // собственные нули за данные прибора.
        int frameSize = repository.FrameSize;
        byte[] buffer = new byte[BinaryLayoutConstants.CanonicalFrameSize];
        for (int index = startIndex; index <= endIndex; index++)
        {
            if (!repository.TryGetRecord(index, out GpToolData record))
            {
                return false;
            }

            WriteSourceSizedRecord(stream, record, buffer, frameSize);
        }

        return true;
    }

    private static void WriteSourceSizedRecord(FileStream stream, GpToolData record, byte[] buffer, int frameSize)
    {
        MemoryMarshal.Write(buffer.AsSpan(), in record);
        stream.Write(buffer, 0, frameSize);
    }

    private static bool ReadExactly(FileStream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static bool IsSupportedDataFileFormat(FileFormat format)
    {
        return format is FileFormat.Bin or FileFormat.Biv or FileFormat.Dev;
    }
}
