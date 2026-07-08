using System.Runtime.InteropServices;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;
using AMK.DataVisualization.IO.DataFiles;

BinaryLayoutVerifier.ThrowIfInvalid();

string testRoot = Path.Combine(Path.GetTempPath(), "AMK.DataVisualization.IO.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);

try
{
    GpToolData autonom = CreateRecord(signature: 151000, frame: 1, temperature: 16.5f, deltaPhase: 1.25f, amplitudeRx1: 11.0f);
    GpToolData lwd = CreateRecord(signature: 241000, frame: 2, temperature: 17.5f, deltaPhase: 2.5f, amplitudeRx1: 42.0f);

    string binPath = Path.Combine(testRoot, "sample.bin");
    string bivPath = Path.Combine(testRoot, "sample.biv");
    string devPath = Path.Combine(testRoot, "sample.DEV");
    string invalidPath = Path.Combine(testRoot, "sample.txt");
    string corruptedPath = Path.Combine(testRoot, "corrupted.bin");

    WriteCanonicalFile(binPath, autonom, lwd);
    WriteCanonicalFile(bivPath, autonom, lwd);
    WriteDevFile(devPath, autonom, lwd);
    File.WriteAllBytes(invalidPath, Serialize(autonom));
    File.WriteAllBytes(corruptedPath, [1, 2, 3]);

    Assert(ToolDataFileReader.DetectFileFormat(binPath) == FileFormat.Bin, ".bin format detection failed.");
    Assert(ToolDataFileReader.DetectFileFormat(bivPath) == FileFormat.Biv, ".biv format detection failed.");
    Assert(ToolDataFileReader.DetectFileFormat(devPath) == FileFormat.Dev, ".DEV format detection failed.");
    Assert(ToolDataFileReader.GetPhysicalFrameSize(FileFormat.Bin) == 240, ".bin physical frame size failed.");
    Assert(ToolDataFileReader.GetPhysicalFrameSize(FileFormat.Dev) == 251, ".DEV physical frame size failed.");

    ValidateLoadedFile(binPath, FileFormat.Bin);
    ValidateLoadedFile(bivPath, FileFormat.Biv);
    ValidateLoadedFile(devPath, FileFormat.Dev);
    ValidateRepository(binPath, autonom, lwd);
    ValidateRangeExport(binPath, devPath, lwd);
    ValidateRealDataFixtures();

    Assert(!ToolDataFileReader.TryValidateDataFile(invalidPath, out DataFileValidationResult invalid) && invalid.Format == FileFormat.Unknown, "Unsupported extension must fail.");
    Assert(!ToolDataFileReader.TryValidateDataFile(corruptedPath, out DataFileValidationResult corrupted) && corrupted.ErrorMessage is not null, "Corrupted size must fail.");

    Console.WriteLine("IO file loader, repository, range export, and real-data smoke tests passed.");
    Console.WriteLine($"bin={ToolDataFileReader.LoadDataFromFile(binPath).Records.Count}; devFrameSize={ToolDataFileReader.GetPhysicalFrameSize(FileFormat.Dev)}");
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

void ValidateLoadedFile(string path, FileFormat expectedFormat)
{
    DataLoadResult result = ToolDataFileReader.LoadDataFromFile(path);
    Assert(result.HasData, "Loaded result must contain data.");
    Assert(result.Records.Count == 2, "Loaded file must contain two records.");
    Assert(result.RecordMetadata.Count == 2, "Metadata count must match record count.");
    Assert(result.LoadedFileName == "sample", "Loaded file name must be path-free and extension-free.");
    Assert(result.Format == expectedFormat, "Loaded format mismatch.");
    Assert(result.Records[0].Signature == 151000, "First signature mismatch.");
    Assert(result.Records[1].Signature == 241000, "Second signature mismatch.");
    Assert(result.RecordMetadata[0].Kind == ToolDataKind.Autonom, "Autonom metadata kind mismatch.");
    Assert(result.RecordMetadata[1].Kind == ToolDataKind.Lwd, "LWD metadata kind mismatch.");
    Assert(result.RecordMetadata[0].Capabilities.TransmitterCount == 5, "151 tool must expose 5 TX.");
    Assert(result.RecordMetadata[1].Capabilities.TransmitterCount == 4, "241 tool must expose 4 TX.");
    Assert(Math.Abs(result.Records[0].GetDeltaPhase(0, 0) - 1.25f) < 0.0001f, "Delta phase payload mismatch.");
    Assert(Math.Abs(result.Records[1].GetAmplitudeRx1(1, 3) - 42.0f) < 0.0001f, "Amplitude payload mismatch.");
}

void ValidateRepository(string binPath, GpToolData autonom, GpToolData lwd)
{
    ToolDataRepository repository = new();
    Assert(repository.DataVersion == 0, "New repository version must start at zero.");
    Assert(!repository.HasData && repository.DataSize == 0, "New repository must be empty.");

    repository.ApplyLoadedData(ToolDataFileReader.LoadDataFromFile(binPath));
    Assert(repository.DataVersion == 1, "ApplyLoadedData must increment data version.");
    Assert(repository.HasData && repository.DataSize == 2, "Repository must expose loaded records.");
    Assert(repository.LoadedFileName == "sample", "Repository loaded file name mismatch.");
    Assert(repository.TryGetRecord(1, out GpToolData secondRecord) && secondRecord.Signature == 241000, "TryGetRecord must return loaded record.");
    Assert(repository.TryGetRecordMetadata(1, out ToolRecordMetadata secondMetadata) && secondMetadata.Kind == ToolDataKind.Lwd, "TryGetRecordMetadata must return loaded metadata.");
    Assert(!repository.TryGetRecord(-1, out _) && !repository.TryGetRecord(2, out _), "Out-of-range record lookup must fail.");

    Assert(repository.TryDescribeRangeMetadata(0, 0, out DataRangeMetadataSummary singleSummary), "Single range metadata must resolve.");
    Assert(singleSummary.HasMetadata && singleSummary.IsHomogeneous && singleSummary.RecordCount == 1, "Single range metadata summary mismatch.");
    Assert(repository.TryAnalyzeExportRange(0, 1, out DataRangeExportAnalysis mixedAnalysis), "Mixed range analysis must resolve.");
    Assert(mixedAnalysis.IsValidRange && mixedAnalysis.CanExportAsCanonicalBin && mixedAnalysis.RequiresAdapterAwarePolicy, "Mixed range must require adapter-aware policy.");
    Assert(repository.TryGetRangeMetadataSummary(0, 0, out ToolRecordMetadata rangeMetadata, out bool isHomogeneous), "Range metadata tuple must resolve.");
    Assert(rangeMetadata.Kind == ToolDataKind.Autonom && isHomogeneous, "Range metadata tuple mismatch.");
    Assert(!repository.TryDescribeRangeMetadata(1, 0, out _), "Invalid range must fail.");

    Assert(repository.TryGetMeasurementView(0, out var measurement) && measurement is not null && measurement.Temperature == 16.5f, "Measurement view mismatch.");
    Assert(repository.TryGetDataView(0, out var accessView) && accessView.TryGetDeltaPhase(0, 0, out float deltaPhase) && Math.Abs(deltaPhase - 1.25f) < 0.0001f, "Access view mismatch.");

    repository.AddData(lwd);
    Assert(repository.DataVersion == 2 && repository.DataSize == 3, "AddData must append and increment version.");
    Assert(repository.TryGetRecordMetadata(2, out ToolRecordMetadata addedMetadata) && addedMetadata.Kind == ToolDataKind.Lwd, "AddData must rebuild canonical metadata.");

    repository.AddDataBatch(Array.Empty<GpToolData>());
    Assert(repository.DataVersion == 2 && repository.DataSize == 3, "Empty AddDataBatch must not increment version.");

    repository.AddParsedData(new ParsedToolFrame(ToolDataKind.Autonom, UniversalCanonicalAdapter.BuildCapabilities(151), autonom));
    Assert(repository.DataVersion == 3 && repository.DataSize == 4, "AddParsedData must append and increment version.");

    repository.ClearData();
    Assert(repository.DataVersion == 4, "ClearData must increment version.");
    Assert(!repository.HasData && repository.DataSize == 0 && repository.LoadedFileName.Length == 0, "ClearData must reset repository state.");

    Assert(!repository.TryLoadDataFromFile(Path.Combine(Path.GetDirectoryName(binPath)!, "missing.bin"), out string? errorMessage), "Failed load must return false.");
    Assert(errorMessage is not null && repository.DataVersion == 5 && !repository.HasData, "Failed TryLoad must clear data and increment version.");
}

void ValidateRangeExport(string binPath, string devPath, GpToolData expectedSecondRecord)
{
    ToolDataRepository repository = new();
    repository.LoadDataFromFile(binPath);

    string savedRangePath = Path.Combine(testRoot, "saved-range.bin");
    Assert(repository.SaveDataRange(savedRangePath, 0, 0), "Repository SaveDataRange must save a valid range.");
    Assert(new FileInfo(savedRangePath).Length == BinaryLayoutConstants.GpToolDataSize, "Saved range must be canonical 240-byte records.");
    DataLoadResult savedRange = ToolDataFileReader.LoadDataFromFile(savedRangePath);
    Assert(savedRange.Records.Count == 1 && savedRange.Records[0].Signature == 151000, "Saved canonical range content mismatch.");

    Assert(!repository.SaveDataRange(Path.Combine(testRoot, "invalid-range.bin"), 1, 0), "Invalid SaveDataRange indices must fail.");
    Assert(!new ToolDataRepository().SaveDataRange(Path.Combine(testRoot, "empty-range.bin"), 0, 0), "SaveDataRange on empty repository must fail.");

    string cutBinPath = Path.Combine(testRoot, "cut.bin");
    Assert(ToolDataRangeExporter.SaveFileExcludingRange(binPath, cutBinPath, 0, 0), "SaveFileExcludingRange must copy remaining .bin frames.");
    Assert(new FileInfo(cutBinPath).Length == BinaryLayoutConstants.GpToolDataSize, "Cut .bin must contain one physical frame.");
    DataLoadResult cutBin = ToolDataFileReader.LoadDataFromFile(cutBinPath);
    Assert(cutBin.Records.Count == 1 && cutBin.Records[0].Signature == expectedSecondRecord.Signature, "Cut .bin content mismatch.");

    string cutDevPath = Path.Combine(testRoot, "cut.DEV");
    Assert(ToolDataRangeExporter.SaveFileExcludingRange(devPath, cutDevPath, 1, 1), "SaveFileExcludingRange must preserve .DEV physical frames.");
    Assert(new FileInfo(cutDevPath).Length == BinaryLayoutConstants.DevPhysicalFrameSize, "Cut .DEV must preserve 251-byte physical frame.");
    DataLoadResult cutDev = ToolDataFileReader.LoadDataFromFile(cutDevPath);
    Assert(cutDev.Records.Count == 1 && cutDev.Records[0].Signature == 151000, "Cut .DEV content mismatch.");

    Assert(!ToolDataRangeExporter.SaveFileExcludingRange(binPath, binPath, 0, 0), "Same source and target must fail.");
    Assert(!ToolDataRangeExporter.SaveFileExcludingRange(binPath, Path.Combine(testRoot, "cut.dev"), 0, 0), "Mismatched source/target formats must fail.");
    Assert(!ToolDataRangeExporter.SaveFileExcludingRange(binPath, Path.Combine(testRoot, "cut-all.bin"), 0, 1), "Removing the whole source file must fail.");
}

void ValidateRealDataFixtures()
{
    string? configuredFixtureRoot = Environment.GetEnvironmentVariable("AMK_DATA_FIXTURES");
    string fixtureRoot = !string.IsNullOrWhiteSpace(configuredFixtureRoot)
        ? configuredFixtureRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AMK_Data");

    if (!Directory.Exists(fixtureRoot))
    {
        Console.WriteLine($"Real-data fixtures skipped: folder not found: {fixtureRoot}");
        return;
    }

    string[] supportedExtensions = [".bin", ".biv", ".dev"];
    string[] dataFiles = Directory
        .EnumerateFiles(fixtureRoot, "*.*", SearchOption.AllDirectories)
        .Where(path => supportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Assert(dataFiles.Length > 0, "Real-data fixture folder must contain supported data files.");

    long totalRecords = 0;
    int binCount = 0;
    int bivCount = 0;
    int devCount = 0;

    foreach (string dataFile in dataFiles)
    {
        DataFileValidationResult validation = ToolDataFileReader.ValidateDataFile(dataFile);
        Assert(validation.IsValid, $"Real data file failed validation: {dataFile}: {validation.ErrorMessage}");
        Assert(validation.FrameCount > 0, $"Real data file must contain at least one frame: {dataFile}");

        ToolDataRepository repository = new();
        repository.LoadDataFromFile(dataFile);
        Assert(repository.HasData, $"Repository must load real data file: {dataFile}");
        Assert(repository.DataSize == validation.FrameCount, $"Repository frame count mismatch: {dataFile}");
        Assert(repository.LoadedFileName == Path.GetFileNameWithoutExtension(dataFile), $"Loaded file name mismatch for real data: {dataFile}");
        Assert(repository.TryAnalyzeExportRange(0, repository.DataSize - 1, out DataRangeExportAnalysis analysis), $"Range analysis failed for real data: {dataFile}");
        Assert(analysis.IsValidRange && analysis.CanExportAsCanonicalBin, $"Real data range must be exportable as canonical bin: {dataFile}");
        Assert(repository.TryGetDataView(0, out var accessView) && accessView.IsValid, $"Real data access view must be valid: {dataFile}");

        totalRecords += repository.DataSize;
        switch (validation.Format)
        {
            case FileFormat.Bin:
                binCount++;
                break;
            case FileFormat.Biv:
                bivCount++;
                break;
            case FileFormat.Dev:
                devCount++;
                break;
        }
    }

    Console.WriteLine($"Real-data fixtures loaded: files={dataFiles.Length}; records={totalRecords}; bin={binCount}; biv={bivCount}; dev={devCount}");
}

static GpToolData CreateRecord(uint signature, uint frame, float temperature, float deltaPhase, float amplitudeRx1)
{
    GpToolData data = new()
    {
        Signature = signature,
        Frame = frame,
        Temperature = temperature
    };

    data.SetDeltaPhase(0, 0, deltaPhase);
    data.SetAmplitudeRx1(1, 3, amplitudeRx1);
    return data;
}

static void WriteCanonicalFile(string path, params GpToolData[] records)
{
    using FileStream stream = File.Create(path);
    foreach (GpToolData record in records)
    {
        byte[] raw = Serialize(record);
        stream.Write(raw, 0, raw.Length);
    }
}

static void WriteDevFile(string path, params GpToolData[] records)
{
    using FileStream stream = File.Create(path);
    foreach (GpToolData record in records)
    {
        stream.Write(new byte[BinaryLayoutConstants.DevHeaderSize]);
        byte[] raw = Serialize(record);
        stream.Write(raw, 0, raw.Length);
    }
}

static byte[] Serialize(GpToolData data)
{
    byte[] bytes = new byte[BinaryLayoutConstants.GpToolDataSize];
    IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
    try
    {
        Marshal.StructureToPtr(data, pointer, fDeleteOld: false);
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
    finally
    {
        Marshal.FreeHGlobal(pointer);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
