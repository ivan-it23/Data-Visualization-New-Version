using AMK.DataVisualization.Charting.Series;

namespace AMK.DataVisualization.Charting.Configuration;

public sealed class SchematicConfigurationEditor
{
    private readonly CurveSelectionService curveSelectionService;

    public SchematicConfigurationEditor(
        CurveSelectionService curveSelectionService,
        int availableTransmitterCount,
        ChartConfiguration existingConfiguration)
    {
        this.curveSelectionService = curveSelectionService;
        Selection = new SchematicSelection(availableTransmitterCount);
        LoadExistingConfiguration(existingConfiguration);
    }

    public SchematicSelection Selection { get; }

    public List<CurveDefinition> AvailableCurves { get; private set; } = [];

    public PresetType CurrentPreset { get; private set; } = PresetType.None;

    public string CurrentPresetDisplayName { get; private set; } = string.Empty;

    public XAxisType XAxisType { get; set; } = XAxisType.FrameNumber;

    public bool CanApply => ConfigurationError.Length == 0;

    public string ConfigurationError
    {
        get
        {
            ChartConfiguration temporary = CreateConfiguration(normalize: false);
            return curveSelectionService.GetConfigurationError(temporary);
        }
    }

    public bool ToggleElement(ElementType type, int index)
    {
        if (type == ElementType.Transmitter && !Selection.SupportsTransmitter(index))
        {
            return false;
        }

        bool switchingFromPreset = Selection.DataFilter != DataTypeFilter.All || CurrentPreset != PresetType.None;
        if (switchingFromPreset)
        {
            FrequencyBand frequency = Selection.Frequency;
            Selection.Clear();
            Selection.Frequency = frequency;
        }

        bool[] values = GetSelectionArray(type);
        if (index < 0 || index >= values.Length)
        {
            return false;
        }

        bool newState = !values[index];
        if (newState && !IsSelectionCompatible(type, index))
        {
            ClearIncompatibleSelections(type);
        }

        values[index] = newState;
        Selection.DataFilter = DataTypeFilter.All;
        CurrentPreset = PresetType.None;
        CurrentPresetDisplayName = string.Empty;
        RefreshAvailableCurves();
        return true;
    }

    public void SetFrequency(FrequencyBand frequency)
    {
        Selection.Frequency = frequency;
        RefreshAvailableCurves();
    }

    public void ApplyPreset(PresetType preset)
    {
        FrequencyBand frequency = Selection.Frequency;
        SmtComputationMode rhoMode = Selection.RhoSmtMode;
        SmtComputationMode phaseMode = Selection.PhaseSmtMode;
        Selection.Clear();
        Selection.Frequency = frequency;
        Selection.RhoSmtMode = rhoMode;
        Selection.PhaseSmtMode = phaseMode;

        CurrentPreset = preset;
        switch (preset)
        {
            case PresetType.AllAmplitudesR1:
                Selection.Receivers[0] = true;
                Selection.DataFilter = DataTypeFilter.AmplitudeOnly;
                CurrentPresetDisplayName = "Амплитуда RX1";
                break;
            case PresetType.AllAmplitudesR2:
                Selection.Receivers[1] = true;
                Selection.DataFilter = DataTypeFilter.AmplitudeOnly;
                CurrentPresetDisplayName = "Амплитуда RX2";
                break;
            case PresetType.AmplitudeRatios:
                Array.Fill(Selection.Receivers, true);
                Selection.DataFilter = DataTypeFilter.RatioOnly;
                CurrentPresetDisplayName = "Отношение амплитуд";
                break;
            case PresetType.PhaseDifferences:
                Array.Fill(Selection.Receivers, true);
                Selection.DataFilter = DataTypeFilter.DifferenceOnly;
                CurrentPresetDisplayName = "Разность фаз";
                break;
            case PresetType.Temperature:
                Selection.DataFilter = DataTypeFilter.TemperatureOnly;
                CurrentPresetDisplayName = "Температура";
                break;
            case PresetType.RhoSmt:
                Selection.DataFilter = DataTypeFilter.RhoSmtOnly;
                CurrentPresetDisplayName = BuildSmtDisplayName("УЭС", Selection.RhoSmtMode);
                break;
            case PresetType.PhaseSmt:
                Selection.DataFilter = DataTypeFilter.PhaseSmtOnly;
                CurrentPresetDisplayName = BuildSmtDisplayName("Симм. фазы", Selection.PhaseSmtMode);
                break;
            case PresetType.SilentAmplitudes:
                Array.Fill(Selection.SilentTransmitters, true);
                Selection.DataFilter = DataTypeFilter.AmplitudeOnly;
                CurrentPresetDisplayName = "Ампл. молч.";
                break;
            case PresetType.SilentPhaseDifference:
                Array.Fill(Selection.SilentTransmitters, true);
                Selection.DataFilter = DataTypeFilter.DifferenceOnly;
                CurrentPresetDisplayName = "Разность фаз молч.";
                break;
            default:
                CurrentPreset = PresetType.None;
                CurrentPresetDisplayName = string.Empty;
                break;
        }

        RefreshAvailableCurves();
    }

    public bool TrySetSmtMode(PresetType preset, SmtComputationMode mode, bool hasMetrologyData, out string warning)
    {
        warning = string.Empty;
        if (mode == SmtComputationMode.FromComputer && !hasMetrologyData)
        {
            warning = "Метрология не была считана.\nДля режима «На компьютере» необходимы метрологические данные.\n\nПодключите прибор или загрузите метрологию из файла (Прибор → Метрология).";
            mode = SmtComputationMode.FromSonde;
        }

        if (preset == PresetType.RhoSmt)
        {
            Selection.RhoSmtMode = mode;
        }
        else if (preset == PresetType.PhaseSmt)
        {
            Selection.PhaseSmtMode = mode;
        }
        else
        {
            return false;
        }

        ApplyPreset(preset);
        return warning.Length == 0;
    }

    public void SetCurveEnabled(string curveId, bool enabled)
    {
        CurveDefinition? curve = AvailableCurves.FirstOrDefault(item => item.Id == curveId);
        if (curve is not null)
        {
            curve.Enabled = enabled;
        }
    }

    public ChartConfiguration BuildConfiguration()
    {
        return curveSelectionService.NormalizeForApplication(CreateConfiguration(normalize: false));
    }

    private void LoadExistingConfiguration(ChartConfiguration existingConfiguration)
    {
        if (existingConfiguration.IsEmpty)
        {
            RefreshAvailableCurves();
            return;
        }

        Selection.Clear();
        Selection.Frequency = existingConfiguration.Curves[0].Frequency;
        Selection.DataFilter = curveSelectionService.DetermineDataFilterFromCurves(existingConfiguration.Curves);
        XAxisType = existingConfiguration.XAxisType;
        CurrentPreset = existingConfiguration.ActivePreset;
        CurrentPresetDisplayName = existingConfiguration.PresetDisplayName;

        foreach (CurveDefinition curve in existingConfiguration.Curves)
        {
            if (curve.DataType == DataType.RhoSmt)
            {
                Selection.RhoSmtMode = curve.SmtMode;
            }
            else if (curve.DataType == DataType.PhaseSmt)
            {
                Selection.PhaseSmtMode = curve.SmtMode;
            }
        }

        RestoreSelectionFromCurves(existingConfiguration.Curves);
        AvailableCurves = existingConfiguration.Curves.Select(curve => curve.Copy()).ToList();
    }

    private void RestoreSelectionFromCurves(IReadOnlyList<CurveDefinition> curves)
    {
        if (Selection.DataFilter != DataTypeFilter.All)
        {
            foreach (CurveDefinition curve in curves)
            {
                switch (curve.DataType)
                {
                    case DataType.AmplitudeRx1:
                        Selection.Receivers[0] = true;
                        break;
                    case DataType.AmplitudeRx2:
                        Selection.Receivers[1] = true;
                        break;
                    case DataType.AmplitudeRatio:
                    case DataType.PhaseDifference:
                        Array.Fill(Selection.Receivers, true);
                        break;
                }

                if (curve.SourceElement == ElementType.SilentTx && curve.ElementIndex >= 0 && curve.ElementIndex < Selection.SilentTransmitters.Length)
                {
                    Selection.SilentTransmitters[curve.ElementIndex] = true;
                }
            }

            return;
        }

        bool hasRx1 = curves.Any(curve => curve.DataType == DataType.AmplitudeRx1);
        bool hasRx2 = curves.Any(curve => curve.DataType == DataType.AmplitudeRx2);
        bool hasRatioOrDifference = curves.Any(curve => curve.DataType is DataType.AmplitudeRatio or DataType.PhaseDifference);
        HashSet<int> transmitters = curves
            .Where(curve => curve.SourceElement == ElementType.Transmitter)
            .Select(curve => curve.ElementIndex)
            .Where(Selection.SupportsTransmitter)
            .ToHashSet();

        if (hasRatioOrDifference)
        {
            Array.Fill(Selection.Receivers, true);
        }
        else if (hasRx1 && !hasRx2 && transmitters.Count == Selection.AvailableTransmitterCount)
        {
            Selection.Receivers[0] = true;
        }
        else if (hasRx2 && !hasRx1 && transmitters.Count == Selection.AvailableTransmitterCount)
        {
            Selection.Receivers[1] = true;
        }
        else if (hasRx1 && hasRx2)
        {
            foreach (int transmitter in transmitters)
            {
                Selection.Transmitters[transmitter] = true;
            }
        }

        foreach (CurveDefinition curve in curves.Where(curve => curve.SourceElement == ElementType.SilentTx))
        {
            if (curve.ElementIndex >= 0 && curve.ElementIndex < Selection.SilentTransmitters.Length)
            {
                Selection.SilentTransmitters[curve.ElementIndex] = true;
            }
        }
    }

    private ChartConfiguration CreateConfiguration(bool normalize)
    {
        ChartConfiguration configuration = new()
        {
            XAxisType = XAxisType,
            ActivePreset = CurrentPreset,
            PresetDisplayName = CurrentPreset switch
            {
                PresetType.RhoSmt => BuildSmtDisplayName("УЭС", Selection.RhoSmtMode),
                PresetType.PhaseSmt => BuildSmtDisplayName("Симм. фазы", Selection.PhaseSmtMode),
                _ => CurrentPresetDisplayName
            }
        };

        foreach (CurveDefinition source in AvailableCurves)
        {
            CurveDefinition curve = source.Copy();
            if (curve.DataType == DataType.RhoSmt)
            {
                curve.SmtMode = Selection.RhoSmtMode;
            }
            else if (curve.DataType == DataType.PhaseSmt)
            {
                curve.SmtMode = Selection.PhaseSmtMode;
            }

            configuration.Curves.Add(curve);
        }

        configuration.UpdateAxisConfiguration();
        return normalize ? curveSelectionService.NormalizeForApplication(configuration) : configuration;
    }

    private void RefreshAvailableCurves()
    {
        AvailableCurves = curveSelectionService.GetAvailableCurves(Selection);
    }

    private bool IsSelectionCompatible(ElementType type, int index)
    {
        return type switch
        {
            ElementType.Transmitter => Selection.SupportsTransmitter(index) && Selection.SelectedReceiverCount == 0,
            ElementType.Receiver => Selection.SelectedTransmitterCount == 0,
            ElementType.SilentTx => Selection.SelectedReceiverCount < SchematicSelection.UiReceiverCount,
            _ => true
        };
    }

    private void ClearIncompatibleSelections(ElementType newType)
    {
        switch (newType)
        {
            case ElementType.Transmitter:
                Array.Fill(Selection.Receivers, false);
                break;
            case ElementType.Receiver:
                Array.Fill(Selection.Transmitters, false);
                if (Selection.SelectedReceiverCount >= 1 && Selection.SelectedSilentCount > 0)
                {
                    Array.Fill(Selection.SilentTransmitters, false);
                }
                break;
            case ElementType.SilentTx when Selection.SelectedReceiverCount == SchematicSelection.UiReceiverCount:
                Array.Fill(Selection.Receivers, false);
                break;
        }
    }

    private bool[] GetSelectionArray(ElementType type)
    {
        return type switch
        {
            ElementType.Transmitter => Selection.Transmitters,
            ElementType.Receiver => Selection.Receivers,
            ElementType.SilentTx => Selection.SilentTransmitters,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static string BuildSmtDisplayName(string baseName, SmtComputationMode mode)
    {
        return $"{baseName} ({(mode == SmtComputationMode.FromComputer ? "на компьютере" : "на приборе")})";
    }
}
