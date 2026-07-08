using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;
using AMK.DataVisualization.IO.DataFiles;

BinaryLayoutVerifier.ThrowIfInvalid();

GpToolData first = CreateRecord(151000, 1, 10.0f);
GpToolData second = CreateRecord(241000, 2, 20.0f);

ToolRecordMetadata firstMetadata = BuildMetadata(first.Signature);
ToolRecordMetadata secondMetadata = BuildMetadata(second.Signature);
GpToolData[] records = [first, second];
ToolRecordMetadata[] metadata = [firstMetadata, secondMetadata];

ValidateSyntheticSeries(records, metadata);
ValidateConfigurationLayer(records, metadata);
ValidateRealDataSeries();

Console.WriteLine("Charting data-series smoke tests passed.");

void ValidateSyntheticSeries(IReadOnlyList<GpToolData> sourceRecords, IReadOnlyList<ToolRecordMetadata> sourceMetadata)
{
    List<List<float>> temperature = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, DataType.Temperature, FrequencyBand.Freq400KHz);
    Assert(temperature.Count == 1, "Temperature must produce one channel.");
    Assert(temperature[0].Count == 2 && NearlyEqual(temperature[0][0], 10.0f) && NearlyEqual(temperature[0][1], 20.0f), "Temperature values mismatch.");

    List<List<float>> phase = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, DataType.PhaseDifference, FrequencyBand.Freq400KHz);
    Assert(phase.Count == 5, "Available transmitter count must be max over records.");
    Assert(NearlyEqual(phase[0][0], ChartDataCalculator.ConvertNormalizedPhaseToMillidegrees(ChartDataCalculator.NormalizePhase(0.001f))), "Phase difference must be normalized and converted to millidegrees.");
    Assert(float.IsNaN(phase[4][1]), "LWD 4TX record must produce missing T5 value in aligned data.");

    List<List<float>> ratio = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, DataType.AmplitudeRatio, FrequencyBand.Freq400KHz);
    Assert(NearlyEqual(ratio[0][0], 2.0f), "Amplitude ratio mismatch.");
    Assert(NearlyEqual(ratio[1][0], 0.0f), "Amplitude ratio must return zero for near-zero RX1 denominator.");

    ChartDataRequest disabledRequest = new(
        DataType.AmplitudeRx1,
        FrequencyBand.Freq400KHz,
        EnabledTransmitters: [true, false, true, true, true],
        MinX: 0.0,
        MaxX: 1.0,
        MinY: -1e6,
        MaxY: 1e6);
    List<List<float>> disabled = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, disabledRequest);
    Assert(disabled.Count == 5 && disabled[1].Count == 0, "Disabled transmitter channel must stay empty, matching old CalculateChartData behavior.");

    ChartDataRequest filteredRequest = new(DataType.Temperature, FrequencyBand.Freq400KHz, null, 0.0, 1.0, 15.0, 100.0);
    List<List<float>> filtered = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, filteredRequest);
    Assert(float.IsNaN(filtered[0][0]) && NearlyEqual(filtered[0][1], 20.0f), "Y filter must preserve alignment with NaN.");

    List<List<float>> silentPhase = ChartDataCalculator.CalculateChartData(sourceRecords, sourceMetadata, DataType.SilentPhaseDifference, FrequencyBand.Freq400KHz);
    Assert(silentPhase.Count == 1 && NearlyEqual(silentPhase[0][0], ChartDataCalculator.ConvertNormalizedPhaseToMillidegrees(ChartDataCalculator.NormalizePhase(0.002f))), "Silent phase difference mismatch.");

    DataIndexWindow window = ChartDataCalculator.GetDataIndexRange(sourceRecords.Count, 0.2, 0.8);
    Assert(window.StartIndex == 0 && window.EndIndex == 1, "Data index window must match old truncation behavior.");
}

void ValidateConfigurationLayer(IReadOnlyList<GpToolData> sourceRecords, IReadOnlyList<ToolRecordMetadata> sourceMetadata)
{
    CurveDefinition defaultCurve = new();
    Assert(defaultCurve.YAxisGroup == 0, "Default curve must target the left axis.");
    Assert(!defaultCurve.Enabled, "Default curve must be disabled.");
    Assert(defaultCurve.SourceElement == ElementType.Transmitter, "Default source element mismatch.");
    Assert(defaultCurve.ElementIndex == 0, "Default element index mismatch.");
    Assert(defaultCurve.DataType == DataType.AmplitudeRx1, "Default data type mismatch.");
    Assert(defaultCurve.Frequency == FrequencyBand.Freq400KHz, "Default frequency mismatch.");
    Assert(defaultCurve.SmtMode == SmtComputationMode.FromSonde, "Default SMT mode mismatch.");
    Assert(NearlyEqual(defaultCurve.ValueScale, 1.0f), "Default valueScale mismatch.");

    CurveDefinition left = new("left", "Left", "mV", ElementType.Transmitter, 0, DataType.AmplitudeRx1, FrequencyBand.Freq400KHz);
    CurveDefinition right = new("right", "Right", "deg", ElementType.Transmitter, 0, DataType.PhaseDifference, FrequencyBand.Freq400KHz)
    {
        YAxisGroup = 1
    };
    CurveDefinition disabled = new("disabled", "Disabled", "ignored", ElementType.Transmitter, 1, DataType.AmplitudeRx2, FrequencyBand.Freq400KHz)
    {
        YAxisGroup = 1,
        Enabled = false
    };
    CurveDefinition ignoredAxis = new("ignored", "Ignored", "ignored", ElementType.Transmitter, 2, DataType.AmplitudeRx2, FrequencyBand.Freq400KHz)
    {
        YAxisGroup = 7
    };

    ChartConfiguration configuration = new();
    configuration.AddCurve(disabled);
    configuration.AddCurve(left);
    configuration.AddCurve(right);
    configuration.AddCurve(ignoredAxis);
    Assert(configuration.HasDualAxis, "Configuration must detect active left and right axes.");
    Assert(configuration.LeftAxisUnits == "mV" && configuration.RightAxisUnits == "deg", "Axis units must come from the first enabled curve on each axis.");
    Assert(configuration.IsPresetActive == false, "No preset must be inactive.");

    GroupedCurves grouped = ChartDataCalculator.GroupCurvesByAxis(configuration.Curves);
    Assert(grouped.LeftAxisCurves.Count == 1 && grouped.LeftAxisCurves[0].Id == "left", "Left axis grouping mismatch.");
    Assert(grouped.RightAxisCurves.Count == 1 && grouped.RightAxisCurves[0].Id == "right", "Right axis grouping mismatch.");

    List<float> scaled = ChartDataCalculator.CalculateSingleCurveData(
        sourceRecords,
        sourceMetadata,
        new CurveDefinition("scaled", "Scaled", "mV", ElementType.Transmitter, 0, DataType.AmplitudeRx1, FrequencyBand.Freq400KHz)
        {
            ValueScale = 2.0f
        });
    Assert(scaled.Count == sourceRecords.Count && NearlyEqual(scaled[0], 20.0f), "valueScale must be applied after calculation.");

    List<float> amplitudeWithNarrowY = ChartDataCalculator.CalculateSingleCurveData(
        sourceRecords,
        sourceMetadata,
        new CurveDefinition("amp", "Amplitude", "mV", ElementType.Transmitter, 0, DataType.AmplitudeRx1, FrequencyBand.Freq400KHz),
        0.0,
        1.0,
        1000.0,
        2000.0);
    Assert(amplitudeWithNarrowY.Count == 2 && NearlyEqual(amplitudeWithNarrowY[0], 10.0f), "Non-RHO/TEMPERATURE single-curve data must ignore Y filter.");

    List<float> rhoFiltered = ChartDataCalculator.CalculateSingleCurveData(
        sourceRecords,
        sourceMetadata,
        new CurveDefinition("rho", "Rho", "ohm*m", ElementType.Transmitter, 0, DataType.RhoSmt, FrequencyBand.Freq400KHz),
        0.0,
        1.0,
        150.0,
        250.0);
    Assert(rhoFiltered.Count == 0, "RHO_SMT single-curve data must remove values outside Y range.");

    List<float> temperatureFiltered = ChartDataCalculator.CalculateSingleCurveData(
        sourceRecords,
        sourceMetadata,
        new CurveDefinition("temperature", "Temperature", "C", ElementType.Transmitter, 0, DataType.Temperature, FrequencyBand.Freq400KHz),
        0.0,
        1.0,
        15.0,
        100.0);
    Assert(temperatureFiltered.Count == 1 && NearlyEqual(temperatureFiltered[0], 20.0f), "TEMPERATURE single-curve data must remove values outside Y range.");

    ConfiguredChartData configuredData = ChartDataCalculator.CalculateChartDataFromConfiguration(sourceRecords, sourceMetadata, configuration);
    Assert(configuredData.LeftAxisData.Count == 1 && configuredData.RightAxisData.Count == 1, "Configured chart must calculate grouped left/right data.");
    Assert(configuredData.LeftAxisData[0].Count == sourceRecords.Count, "Configured left-axis series must preserve X alignment.");
    Assert(configuredData.RightAxisData[0].Count == sourceRecords.Count, "Configured right-axis series must preserve X alignment.");

    configuration.RemoveCurve("right");
    Assert(!configuration.HasDualAxis && configuration.RightAxisUnits == string.Empty, "Removing the only right-axis curve must clear right-axis state.");

    configuration.ActivePreset = PresetType.PhaseDifferences;
    Assert(configuration.IsPresetActive, "Concrete preset must be active.");
    configuration.ActivePreset = PresetType.Custom;
    Assert(!configuration.IsPresetActive, "Custom preset must not count as active.");
}

void ValidateRealDataSeries()
{
    string? configuredFixtureRoot = Environment.GetEnvironmentVariable("AMK_DATA_FIXTURES");
    string fixtureRoot = !string.IsNullOrWhiteSpace(configuredFixtureRoot)
        ? configuredFixtureRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AMK_Data");

    if (!Directory.Exists(fixtureRoot))
    {
        Console.WriteLine($"Charting real-data smoke skipped: folder not found: {fixtureRoot}");
        return;
    }

    string? firstDataFile = Directory
        .EnumerateFiles(fixtureRoot, "*.bin", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

    if (firstDataFile is null)
    {
        Console.WriteLine($"Charting real-data smoke skipped: no .bin files in {fixtureRoot}");
        return;
    }

    ToolDataRepository repository = new();
    repository.LoadDataFromFile(firstDataFile);
    Assert(repository.HasData, "Real-data repository must load at least one record.");

    List<List<float>> temperature = ChartDataCalculator.CalculateChartData(repository.Records, repository.RecordMetadata, DataType.Temperature, FrequencyBand.Freq400KHz);
    Assert(temperature.Count == 1 && temperature[0].Count == repository.DataSize, "Real-data temperature series must align with record count.");

    List<List<float>> phase = ChartDataCalculator.CalculateChartData(repository.Records, repository.RecordMetadata, DataType.PhaseDifference, FrequencyBand.Freq400KHz);
    Assert(phase.Count is >= 3 and <= BinaryLayoutConstants.MaxTransmitterCount, "Real-data phase series channel count must be transmitter-aware.");
    foreach (List<float> channel in phase)
    {
        Assert(channel.Count == repository.DataSize, "Real-data phase series must preserve X alignment for every enabled channel.");
    }

    Console.WriteLine($"Charting real-data series loaded: file={Path.GetFileName(firstDataFile)}; records={repository.DataSize}; channels={phase.Count}");
}

static GpToolData CreateRecord(uint signature, uint frame, float temperature)
{
    GpToolData data = new()
    {
        Signature = signature,
        Frame = frame,
        Temperature = temperature
    };

    for (int frequency = 0; frequency < BinaryLayoutConstants.FrequencyCount; frequency++)
    {
        for (int transmitter = 0; transmitter < BinaryLayoutConstants.MaxTransmitterCount; transmitter++)
        {
            data.SetDeltaPhase(frequency, transmitter, 0.001f * (transmitter + 1));
            data.SetPhaseSmt(frequency, transmitter, 0.002f * (transmitter + 1));
            data.SetRhoSmt(frequency, transmitter, 100.0f + transmitter);
            data.SetAmplitudeRx1(frequency, transmitter, 10.0f + transmitter);
            data.SetAmplitudeRx2(frequency, transmitter, 20.0f + transmitter);
        }

        data.SetZeroAmplitudeRx1(frequency, 3.0f + frequency);
        data.SetZeroAmplitudeRx2(frequency, 6.0f + frequency);
        data.SetZeroDeltaPhase(frequency, 0.002f + frequency * 0.001f);
    }

    data.SetAmplitudeRx1(0, 1, 1e-12f);
    data.SetAmplitudeRx2(0, 1, 5.0f);
    return data;
}

static ToolRecordMetadata BuildMetadata(uint signature)
{
    uint toolCode = ToolDataAdapterRegistry.DecodeToolCode(signature);
    ToolDataKind kind = UniversalCanonicalAdapter.DetermineKindFromToolCode(toolCode);
    ToolCapabilities capabilities = UniversalCanonicalAdapter.BuildCapabilities(toolCode);
    return new ToolRecordMetadata(kind, capabilities);
}

static bool NearlyEqual(float actual, float expected, float tolerance = 0.001f)
{
    return MathF.Abs(actual - expected) <= tolerance;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

