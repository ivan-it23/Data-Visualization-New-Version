using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Access;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.IO.DataFiles;

public sealed class ToolDataRepository
{
    private readonly List<GpToolData> records = [];
    private readonly List<ToolRecordMetadata> recordMetadata = [];
    private string loadedFileName = string.Empty;
    private string loadedFilePath = string.Empty;
    private FileFormat loadedFileFormat = FileFormat.Unknown;
    private ulong dataVersion;

    public IReadOnlyList<GpToolData> Records => records;

    public IReadOnlyList<ToolRecordMetadata> RecordMetadata => recordMetadata;

    public string LoadedFileName => loadedFileName;

    public string LoadedFilePath => loadedFilePath;

    public FileFormat LoadedFileFormat => loadedFileFormat;

    public ulong DataVersion => dataVersion;

    public bool HasData => records.Count > 0;

    public int DataSize => records.Count;

    /// <summary>
    /// Bytes of the incomplete record the loaded file ended with, or zero. Live
    /// recording appends whole frames, so this only ever describes a file source.
    /// </summary>
    public int LoadedTrailingByteCount { get; private set; }

    /// <summary>
    /// Complete frames of the loaded file that failed the canonical parse and were
    /// left out of the data.
    /// </summary>
    public int LoadedSkippedFrameCount { get; private set; }

    /// <summary>
    /// Физический размер кадра источника: 240 байт у старой прошивки, 320 у
    /// новой. Кадры в памяти всегда приведены к 320 байтам, но на диск уходит
    /// ровно столько, сколько отдал прибор, — иначе файл перестанет быть
    /// копией того, что было записано.
    /// </summary>
    public int FrameSize { get; private set; } = BinaryLayoutConstants.LegacyFrameSize;

    public void LoadDataFromFile(string fileName)
    {
        ApplyLoadedData(ToolDataFileReader.LoadDataFromFile(fileName), fileName);
    }

    public bool TryLoadDataFromFile(string fileName, out string? errorMessage)
    {
        if (!ToolDataFileReader.TryLoadDataFromFile(fileName, out DataLoadResult? result, out errorMessage) || result is null)
        {
            ClearData();
            return false;
        }

        ApplyLoadedData(result, fileName);
        return HasData;
    }

    public void ApplyLoadedData(DataLoadResult loadedData, string? sourceFilePath = null)
    {
        if (loadedData.Records.Count != loadedData.RecordMetadata.Count)
        {
            throw new InvalidDataException("Record and metadata counts must match.");
        }

        records.Clear();
        recordMetadata.Clear();
        records.AddRange(loadedData.Records);
        recordMetadata.AddRange(loadedData.RecordMetadata);
        loadedFileName = loadedData.LoadedFileName;
        loadedFilePath = string.IsNullOrWhiteSpace(sourceFilePath) ? string.Empty : Path.GetFullPath(sourceFilePath);
        loadedFileFormat = loadedData.Format;
        LoadedTrailingByteCount = loadedData.TrailingByteCount;
        LoadedSkippedFrameCount = loadedData.SkippedFrameCount;
        FrameSize = loadedData.FrameSize;
        dataVersion++;
        InvalidateDerivedCaches();
    }

    public void ClearData()
    {
        records.Clear();
        recordMetadata.Clear();
        loadedFileName = string.Empty;
        loadedFilePath = string.Empty;
        loadedFileFormat = FileFormat.Unknown;
        LoadedTrailingByteCount = 0;
        LoadedSkippedFrameCount = 0;
        FrameSize = BinaryLayoutConstants.LegacyFrameSize;
        dataVersion++;
        InvalidateDerivedCaches();
    }

    public void AddParsedData(ParsedToolFrame parsedData)
    {
        AppendParsedFrame(parsedData);
        dataVersion++;
        InvalidateDerivedCaches();
    }

    public void AddData(GpToolData newData)
    {
        AppendCanonicalRecord(newData);
        dataVersion++;
        InvalidateDerivedCaches();
    }

    public void AddDataBatch(IEnumerable<GpToolData> newDataBatch)
    {
        int appendedCount = 0;
        foreach (GpToolData record in newDataBatch)
        {
            AppendCanonicalRecord(record);
            appendedCount++;
        }

        if (appendedCount > 0)
        {
            dataVersion++;
            InvalidateDerivedCaches();
        }
    }

    public bool TryGetRecord(int index, out GpToolData record)
    {
        if (index < 0 || index >= records.Count)
        {
            record = default;
            return false;
        }

        record = records[index];
        return true;
    }

    public bool TryGetRecordMetadata(int index, out ToolRecordMetadata metadata)
    {
        if (index < 0 || index >= recordMetadata.Count)
        {
            metadata = ToolRecordMetadata.Empty;
            return false;
        }

        metadata = recordMetadata[index];
        return true;
    }

    public bool TryDescribeRangeMetadata(int startIndex, int endIndex, out DataRangeMetadataSummary summary)
    {
        summary = DataRangeMetadataSummary.Empty;

        if (!HasData || startIndex > endIndex || startIndex < 0 || endIndex >= DataSize)
        {
            return false;
        }

        if (!TryGetRecordMetadata(startIndex, out ToolRecordMetadata firstMetadata))
        {
            return false;
        }

        bool isHomogeneous = true;
        for (int index = startIndex + 1; index <= endIndex; index++)
        {
            if (!TryGetRecordMetadata(index, out ToolRecordMetadata currentMetadata))
            {
                summary = DataRangeMetadataSummary.Empty;
                return false;
            }

            if (!EqualityComparer<ToolRecordMetadata>.Default.Equals(currentMetadata, firstMetadata))
            {
                isHomogeneous = false;
            }
        }

        summary = new DataRangeMetadataSummary(firstMetadata, endIndex - startIndex + 1, HasMetadata: true, isHomogeneous);
        return true;
    }

    public bool TryAnalyzeExportRange(int startIndex, int endIndex, out DataRangeExportAnalysis analysis)
    {
        analysis = DataRangeExportAnalysis.Empty;
        if (!TryDescribeRangeMetadata(startIndex, endIndex, out DataRangeMetadataSummary metadataSummary))
        {
            return false;
        }

        analysis = new DataRangeExportAnalysis(
            metadataSummary,
            IsValidRange: true,
            CanExportAsCanonicalBin: metadataSummary.HasMetadata,
            RequiresAdapterAwarePolicy: !metadataSummary.IsHomogeneous);
        return true;
    }

    public bool TryGetRangeMetadataSummary(int startIndex, int endIndex, out ToolRecordMetadata metadata, out bool isHomogeneous)
    {
        metadata = ToolRecordMetadata.Empty;
        isHomogeneous = false;

        if (!TryDescribeRangeMetadata(startIndex, endIndex, out DataRangeMetadataSummary summary) || !summary.HasMetadata)
        {
            return false;
        }

        metadata = summary.Metadata;
        isHomogeneous = summary.IsHomogeneous;
        return true;
    }

    public bool TryGetMeasurementView(int index, out MeasurementView? measurementView)
    {
        measurementView = null;
        if (!TryGetRecord(index, out GpToolData record))
        {
            return false;
        }

        ToolRecordMetadata metadata = TryGetRecordMetadata(index, out ToolRecordMetadata foundMetadata)
            ? foundMetadata
            : BuildCanonicalMetadata(record);
        measurementView = new MeasurementView(record, metadata);
        return true;
    }

    public bool TryGetDataView(int index, out ToolDataAccessView accessView)
    {
        accessView = default;
        if (!TryGetMeasurementView(index, out MeasurementView? measurementView) || measurementView is null)
        {
            return false;
        }

        accessView = measurementView.CreateAccessView();
        return true;
    }

    public bool SaveDataRange(string fileName, int startIndex, int endIndex)
    {
        return ToolDataRangeExporter.SaveDataRange(this, fileName, startIndex, endIndex);
    }

    private void AppendParsedFrame(ParsedToolFrame parsedFrame)
    {
        records.Add(parsedFrame.CanonicalData);
        recordMetadata.Add(new ToolRecordMetadata(parsedFrame.Kind, parsedFrame.Capabilities));
        TrackFrameSize(parsedFrame.Capabilities);
    }

    private void AppendCanonicalRecord(GpToolData record)
    {
        ToolRecordMetadata metadata = BuildCanonicalMetadata(record);
        records.Add(record);
        recordMetadata.Add(metadata);
        TrackFrameSize(metadata.Capabilities);
    }

    /// <summary>
    /// Размер кадра задаёт первая попавшая в хранилище запись: источник у
    /// хранилища всегда один, а смена источника его очищает.
    /// </summary>
    private void TrackFrameSize(ToolCapabilities capabilities)
    {
        if (records.Count == 1 && capabilities.FrameSize > 0)
        {
            FrameSize = capabilities.FrameSize;
        }
    }

    private static ToolRecordMetadata BuildCanonicalMetadata(GpToolData record)
    {
        ToolSignatureInfo info = ToolSignature.Decode(record.Signature);
        ToolCapabilities capabilities = UniversalCanonicalAdapter.BuildCapabilities(info);
        return new ToolRecordMetadata(capabilities.Kind, capabilities);
    }

    private void InvalidateDerivedCaches()
    {
        // Chart-derived caches are ported with the Charting layer; dataVersion is already advanced here.
    }
}

