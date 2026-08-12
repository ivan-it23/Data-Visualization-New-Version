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
    int FrameCount,
    int TrailingByteCount = 0)
{
    /// <summary>
    /// Bytes after the last complete frame. A device RAM dump can stop in the
    /// middle of a record, which leaves a partial frame that carries no usable
    /// measurement and is skipped.
    /// </summary>
    public bool HasTrailingPartialFrame => TrailingByteCount > 0;

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
    int PhysicalFrameSize,
    int TrailingByteCount = 0,
    int SkippedFrameCount = 0)
{
    public bool HasData => Records.Count > 0;

    public bool HasTrailingPartialFrame => TrailingByteCount > 0;

    /// <summary>
    /// Complete frames that did not pass the canonical parse and were left out.
    /// A single damaged frame must not cost the user the whole file, but it does
    /// have to be reported.
    /// </summary>
    public bool HasSkippedFrames => SkippedFrameCount > 0;
}
