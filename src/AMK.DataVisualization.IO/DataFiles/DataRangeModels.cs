using AMK.DataVisualization.Core;

namespace AMK.DataVisualization.IO.DataFiles;

public sealed record DataRangeMetadataSummary(
    ToolRecordMetadata Metadata,
    int RecordCount,
    bool HasMetadata,
    bool IsHomogeneous)
{
    public static DataRangeMetadataSummary Empty { get; } = new(ToolRecordMetadata.Empty, 0, false, false);
}

public sealed record DataRangeExportAnalysis(
    DataRangeMetadataSummary MetadataSummary,
    bool IsValidRange,
    bool CanExportAsCanonicalBin,
    bool RequiresAdapterAwarePolicy)
{
    public static DataRangeExportAnalysis Empty { get; } = new(DataRangeMetadataSummary.Empty, false, false, false);
}
