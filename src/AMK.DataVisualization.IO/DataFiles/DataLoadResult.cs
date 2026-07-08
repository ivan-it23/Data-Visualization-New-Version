using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.IO.DataFiles;

public sealed record DataFileValidationResult(
    bool IsValid,
    string? ErrorMessage,
    FileFormat Format,
    uint Signature,
    int PhysicalFrameSize,
    long FileSize,
    int FrameCount)
{
    public static DataFileValidationResult Invalid(string errorMessage, FileFormat format = FileFormat.Unknown)
    {
        return new DataFileValidationResult(false, errorMessage, format, 0, 0, 0, 0);
    }
}

public sealed record DataLoadResult(
    IReadOnlyList<GpToolData> Records,
    IReadOnlyList<ToolRecordMetadata> RecordMetadata,
    string LoadedFileName,
    FileFormat Format,
    int PhysicalFrameSize)
{
    public bool HasData => Records.Count > 0;
}
