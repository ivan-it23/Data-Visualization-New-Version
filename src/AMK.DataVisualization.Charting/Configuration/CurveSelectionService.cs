using AMK.DataVisualization.Charting.Series;

namespace AMK.DataVisualization.Charting.Configuration;

public sealed class CurveSelectionService
{
    private const int SilentColor = 0x007B7B7B;
    private const int TemperatureColor = 0x00447CB0;
    private static readonly int[] LeftAxisColors =
    [
        Rgb(64, 132, 92),
        Rgb(184, 160, 74),
        Rgb(168, 82, 82),
        Rgb(60, 108, 160),
        Rgb(122, 94, 146)
    ];
    private static readonly int[] RightAxisColors =
    [
        Rgb(76, 140, 108),
        Rgb(168, 148, 88),
        Rgb(150, 92, 92),
        Rgb(76, 118, 168),
        Rgb(142, 90, 118)
    ];
    private static readonly int[] ExtendedPalette =
    [
        Rgb(64, 132, 92),
        Rgb(184, 160, 74),
        Rgb(168, 82, 82),
        Rgb(60, 108, 160),
        Rgb(122, 94, 146),
        Rgb(70, 128, 138),
        Rgb(126, 94, 152),
        Rgb(140, 94, 68),
        Rgb(96, 142, 82),
        Rgb(146, 86, 118),
        Rgb(84, 116, 134),
        Rgb(130, 108, 86)
    ];

    public List<CurveDefinition> GetAvailableCurves(SchematicSelection selection)
    {
        List<CurveDefinition> curves = [];
        if (!selection.IsValid)
        {
            return curves;
        }

        int selectedReceivers = selection.SelectedReceiverCount;
        int selectedTransmitters = selection.SelectedTransmitterCount;
        int selectedSilent = selection.SelectedSilentCount;

        if (selection.DataFilter != DataTypeFilter.All)
        {
            if (selectedSilent > 0 && selectedReceivers == 0 && selectedTransmitters == 0)
            {
                AddSilentTransmitterCurves(curves, selection);
                RefreshCurveColors(curves);
                return curves;
            }

            switch (selection.DataFilter)
            {
                case DataTypeFilter.RatioOnly:
                case DataTypeFilter.DifferenceOnly:
                    AddTwoReceiversCurves(curves, selection);
                    break;
                case DataTypeFilter.AmplitudeOnly:
                case DataTypeFilter.PhaseOnly:
                    AddSingleReceiverCurves(curves, selection);
                    break;
                case DataTypeFilter.TemperatureOnly:
                    curves.Add(CreateTemperatureCurve(selection.Frequency));
                    break;
                case DataTypeFilter.RhoSmtOnly:
                    AddSmtCurves(curves, selection, DataType.RhoSmt, selection.RhoSmtMode);
                    break;
                case DataTypeFilter.PhaseSmtOnly:
                    AddSmtCurves(curves, selection, DataType.PhaseSmt, selection.PhaseSmtMode);
                    break;
                case DataTypeFilter.RhoAttSmtOnly:
                    AddSmtCurves(curves, selection, DataType.RhoAttSmt, selection.RhoAttSmtMode);
                    break;
                case DataTypeFilter.AttDbSmtOnly:
                    AddSmtCurves(curves, selection, DataType.AttDbSmt, selection.AttDbSmtMode);
                    break;
            }
        }
        else
        {
            if (selectedReceivers == 2)
            {
                AddTwoReceiversCurves(curves, selection);
            }
            else if (selectedReceivers == 1)
            {
                AddSingleReceiverCurves(curves, selection);
            }

            if (selectedTransmitters > 0)
            {
                AddTransmitterCurves(curves, selection);
            }

            if (selectedSilent > 0)
            {
                AddSilentTransmitterCurves(curves, selection);
            }
        }

        RefreshCurveColors(curves);
        return curves;
    }

    public string GetConfigurationError(ChartConfiguration configuration)
    {
        if (configuration.Curves.Count == 0)
        {
            return "No curves selected for display";
        }

        int enabledCount = configuration.Curves.Count(curve => curve.Enabled);
        if (enabledCount == 0)
        {
            return "All curves are disabled";
        }

        if (configuration.HasDualAxis)
        {
            bool hasLeft = configuration.Curves.Any(curve => curve.Enabled && curve.YAxisGroup == 0);
            bool hasRight = configuration.Curves.Any(curve => curve.Enabled && curve.YAxisGroup == 1);
            if (!hasLeft || !hasRight)
            {
                return "Dual axis requires curves on both axes";
            }
        }

        return enabledCount > 64
            ? "Too many curves for display (maximum 64)"
            : string.Empty;
    }

    public bool ValidateConfiguration(ChartConfiguration configuration)
    {
        return GetConfigurationError(configuration).Length == 0;
    }

    public ChartConfiguration NormalizeForApplication(ChartConfiguration configuration)
    {
        ChartConfiguration normalized = configuration.Copy();
        RefreshCurveColors(normalized.Curves);
        if (normalized.ActivePreset == PresetType.SilentAmplitudes)
        {
            foreach (CurveDefinition curve in normalized.Curves)
            {
                if (curve.DataType is DataType.SilentAmplitudeRx1 or DataType.SilentAmplitudeRx2)
                {
                    curve.Units = "uV";
                    curve.ValueScale = 1000.0f;
                }
            }
        }

        normalized.UpdateAxisConfiguration();
        return normalized;
    }

    public ChartConfiguration BuildFullCurveSet(FrequencyBand frequency)
    {
        ChartConfiguration configuration = new();
        const int transmitterCount = SchematicSelection.UiTransmitterCount;

        for (int transmitter = 0; transmitter < transmitterCount; transmitter++)
        {
            CurveDefinition curve = CreateDifferenceCurve(transmitter, frequency);
            curve.YAxisGroup = 0;
            curve.Enabled = false;
            curve.Color = AssignSmartColor(configuration.Curves, transmitter, 0);
            configuration.Curves.Add(curve);
        }

        for (int transmitter = 0; transmitter < transmitterCount; transmitter++)
        {
            for (int receiver = 0; receiver < SchematicSelection.UiReceiverCount; receiver++)
            {
                CurveDefinition curve = CreateAmplitudeCurve(transmitter, receiver, frequency);
                curve.YAxisGroup = 1;
                curve.Enabled = false;
                curve.Color = AssignSmartColor(configuration.Curves, transmitter, 1);
                configuration.Curves.Add(curve);
            }
        }

        for (int transmitter = 0; transmitter < transmitterCount; transmitter++)
        {
            CurveDefinition curve = CreateRatioCurve(transmitter, frequency);
            curve.YAxisGroup = 0;
            curve.Enabled = false;
            curve.Color = AssignSmartColor(configuration.Curves, transmitter, 0);
            configuration.Curves.Add(curve);
        }

        for (int transmitter = 0; transmitter < transmitterCount; transmitter++)
        {
            CurveDefinition curve = CreatePhaseSmtCurve(transmitter, frequency);
            curve.YAxisGroup = 0;
            curve.Enabled = false;
            curve.Color = AssignSmartColor(configuration.Curves, transmitter, 0);
            configuration.Curves.Add(curve);
        }

        for (int transmitter = 0; transmitter < transmitterCount; transmitter++)
        {
            CurveDefinition curve = CreateRhoSmtCurve(transmitter, frequency);
            curve.YAxisGroup = 0;
            curve.Enabled = false;
            curve.Color = AssignSmartColor(configuration.Curves, transmitter, 0);
            configuration.Curves.Add(curve);
        }

        for (int receiver = 0; receiver < SchematicSelection.UiSilentCount; receiver++)
        {
            CurveDefinition curve = CreateSilentAmplitudeCurve(receiver, frequency);
            curve.YAxisGroup = 1;
            curve.Enabled = false;
            curve.Color = AssignSmartColor(configuration.Curves, receiver, 1);
            configuration.Curves.Add(curve);
        }

        CurveDefinition silentDifference = CreateSilentPhaseDifferenceCurve(frequency);
        silentDifference.YAxisGroup = 0;
        silentDifference.Enabled = false;
        silentDifference.Color = AssignSmartColor(configuration.Curves, 0, 0);
        configuration.Curves.Add(silentDifference);

        CurveDefinition temperature = CreateTemperatureCurve(frequency);
        temperature.Enabled = false;
        configuration.Curves.Add(temperature);
        return configuration;
    }

    public DataTypeFilter DetermineDataFilterFromCurves(IReadOnlyList<CurveDefinition> curves)
    {
        if (curves.Count == 0)
        {
            return DataTypeFilter.All;
        }

        bool hasAmplitudes = false;
        bool hasPhases = false;
        bool hasRatios = false;
        bool hasDifferences = false;
        bool hasSilentAmplitudes = false;
        bool hasSilentDifferences = false;
        bool hasRhoSmt = false;
        bool hasPhaseSmt = false;
        bool hasTemperature = false;
        bool hasRhoAttSmt = false;
        bool hasAttDbSmt = false;

        foreach (CurveDefinition curve in curves)
        {
            switch (curve.DataType)
            {
                case DataType.AmplitudeRx1:
                case DataType.AmplitudeRx2:
                    hasAmplitudes = true;
                    break;
                case DataType.SilentAmplitudeRx1:
                case DataType.SilentAmplitudeRx2:
                    hasSilentAmplitudes = true;
                    break;
                case DataType.SilentPhaseDifference:
                    hasSilentDifferences = true;
                    break;
                case DataType.PhaseSmt:
                    hasPhaseSmt = true;
                    break;
                case DataType.AmplitudeRatio:
                    hasRatios = true;
                    break;
                case DataType.PhaseDifference:
                    hasDifferences = true;
                    break;
                case DataType.RhoSmt:
                    hasRhoSmt = true;
                    break;
                case DataType.RhoAttSmt:
                    hasRhoAttSmt = true;
                    break;
                case DataType.AttDbSmt:
                    hasAttDbSmt = true;
                    break;
                case DataType.Temperature:
                    hasTemperature = true;
                    break;
            }
        }

        // Виды данных новой прошивки не смешиваются с остальными: набор с ними
        // распознаётся либо как «только амплитудные УЭС», либо как «только
        // затухания», либо как произвольный.
        if (hasRhoAttSmt || hasAttDbSmt)
        {
            bool hasLegacyKinds = hasAmplitudes
                || hasPhases
                || hasRatios
                || hasDifferences
                || hasSilentAmplitudes
                || hasSilentDifferences
                || hasRhoSmt
                || hasPhaseSmt
                || hasTemperature;
            if (hasLegacyKinds || (hasRhoAttSmt && hasAttDbSmt))
            {
                return DataTypeFilter.All;
            }

            return hasRhoAttSmt ? DataTypeFilter.RhoAttSmtOnly : DataTypeFilter.AttDbSmtOnly;
        }

        if (hasRhoSmt && !hasPhaseSmt && !hasDifferences && !hasRatios && !hasAmplitudes && !hasPhases && !hasSilentAmplitudes && !hasTemperature)
        {
            return DataTypeFilter.RhoSmtOnly;
        }

        if (hasPhaseSmt && !hasRhoSmt && !hasDifferences && !hasRatios && !hasAmplitudes && !hasPhases && !hasSilentAmplitudes && !hasTemperature)
        {
            return DataTypeFilter.PhaseSmtOnly;
        }

        if (hasRatios && !hasDifferences && !hasAmplitudes && !hasPhases && !hasSilentAmplitudes && !hasRhoSmt && !hasPhaseSmt && !hasTemperature)
        {
            return DataTypeFilter.RatioOnly;
        }

        if (hasDifferences && !hasRatios && !hasAmplitudes && !hasPhases && !hasSilentAmplitudes && !hasRhoSmt && !hasPhaseSmt && !hasTemperature)
        {
            return DataTypeFilter.DifferenceOnly;
        }

        if (hasSilentDifferences && !hasRatios && !hasAmplitudes && !hasPhases && !hasSilentAmplitudes && !hasDifferences && !hasRhoSmt && !hasPhaseSmt && !hasTemperature)
        {
            return DataTypeFilter.DifferenceOnly;
        }

        if (hasAmplitudes && !hasPhases && !hasRatios && !hasDifferences && !hasSilentAmplitudes && !hasRhoSmt && !hasPhaseSmt && !hasTemperature)
        {
            return DataTypeFilter.AmplitudeOnly;
        }

        if (hasPhases && !hasAmplitudes && !hasRatios && !hasDifferences && !hasSilentAmplitudes && !hasRhoSmt && !hasPhaseSmt && !hasTemperature)
        {
            return DataTypeFilter.PhaseOnly;
        }

        return hasTemperature && !hasAmplitudes && !hasPhases && !hasRatios && !hasDifferences && !hasSilentAmplitudes && !hasRhoSmt && !hasPhaseSmt
            ? DataTypeFilter.TemperatureOnly
            : DataTypeFilter.All;
    }

    private void AddTwoReceiversCurves(List<CurveDefinition> curves, SchematicSelection selection)
    {
        bool addRatios = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.RatioOnly;
        bool addDifferences = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.DifferenceOnly;
        bool singleType = addRatios != addDifferences;

        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            if (addDifferences)
            {
                CurveDefinition curve = CreateDifferenceCurve(transmitter, selection.Frequency);
                curve.YAxisGroup = 0;
                curve.Color = AssignSmartColor(curves, transmitter, curve.YAxisGroup);
                curves.Add(curve);
            }
        }

        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            if (addRatios)
            {
                CurveDefinition curve = CreateRatioCurve(transmitter, selection.Frequency);
                curve.YAxisGroup = singleType ? 0 : 1;
                curve.Color = AssignSmartColor(curves, transmitter, curve.YAxisGroup);
                curves.Add(curve);
            }
        }
    }

    private void AddSingleReceiverCurves(List<CurveDefinition> curves, SchematicSelection selection)
    {
        int receiverIndex = Array.FindIndex(selection.Receivers, selected => selected);
        if (receiverIndex < 0 && selection.DataFilter != DataTypeFilter.All)
        {
            receiverIndex = 0;
        }

        if (receiverIndex < 0)
        {
            return;
        }

        bool addAmplitudes = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.AmplitudeOnly;
        bool addPhases = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.PhaseOnly;
        bool singleType = addAmplitudes != addPhases;

        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            if (addPhases)
            {
                CurveDefinition curve = CreateDifferenceCurve(transmitter, selection.Frequency);
                curve.YAxisGroup = 0;
                curve.Color = AssignSmartColor(curves, transmitter, curve.YAxisGroup);
                curves.Add(curve);
            }
        }

        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            if (addAmplitudes)
            {
                CurveDefinition curve = CreateAmplitudeCurve(transmitter, receiverIndex, selection.Frequency);
                curve.YAxisGroup = singleType ? 0 : 1;
                curve.Color = AssignSmartColor(curves, transmitter, curve.YAxisGroup);
                curves.Add(curve);
            }
        }
    }

    private void AddTransmitterCurves(List<CurveDefinition> curves, SchematicSelection selection)
    {
        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            if (!selection.Transmitters[transmitter])
            {
                continue;
            }

            CurveDefinition phase = CreateDifferenceCurve(transmitter, selection.Frequency);
            phase.YAxisGroup = 0;
            phase.Color = AssignSmartColor(curves, transmitter, phase.YAxisGroup);
            curves.Add(phase);

            for (int receiver = 0; receiver < SchematicSelection.UiReceiverCount; receiver++)
            {
                CurveDefinition amplitude = CreateAmplitudeCurve(transmitter, receiver, selection.Frequency);
                amplitude.YAxisGroup = 1;
                amplitude.Color = AssignSmartColor(curves, transmitter, amplitude.YAxisGroup);
                curves.Add(amplitude);
            }
        }
    }

    private void AddSilentTransmitterCurves(List<CurveDefinition> curves, SchematicSelection selection)
    {
        bool addAmplitudes = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.AmplitudeOnly;
        bool addDifferences = selection.DataFilter is DataTypeFilter.All or DataTypeFilter.DifferenceOnly or DataTypeFilter.PhaseOnly;
        bool hasPhases = curves.Any(curve => curve.DataType is DataType.PhaseSmt or DataType.PhaseDifference or DataType.SilentPhaseDifference);

        if (addDifferences && selection.SilentTransmitters.All(selected => selected))
        {
            CurveDefinition difference = CreateSilentPhaseDifferenceCurve(selection.Frequency);
            difference.YAxisGroup = 0;
            difference.Color = AssignSmartColor(curves, 0, difference.YAxisGroup);
            curves.Add(difference);
            hasPhases = true;
        }

        if (!addAmplitudes)
        {
            return;
        }

        for (int receiver = 0; receiver < SchematicSelection.UiSilentCount; receiver++)
        {
            if (!selection.SilentTransmitters[receiver])
            {
                continue;
            }

            CurveDefinition amplitude = CreateSilentAmplitudeCurve(receiver, selection.Frequency);
            amplitude.YAxisGroup = hasPhases ? 1 : 0;
            amplitude.Color = AssignSmartColor(curves, receiver, amplitude.YAxisGroup);
            curves.Add(amplitude);
        }
    }

    private void AddSmtCurves(List<CurveDefinition> curves, SchematicSelection selection, DataType type, SmtComputationMode mode)
    {
        for (int transmitter = 0; transmitter < selection.AvailableTransmitterCount; transmitter++)
        {
            CurveDefinition curve = type switch
            {
                DataType.RhoSmt => CreateRhoSmtCurve(transmitter, selection.Frequency),
                DataType.RhoAttSmt => CreateRhoAttSmtCurve(transmitter, selection.Frequency),
                DataType.AttDbSmt => CreateAttDbSmtCurve(transmitter, selection.Frequency),
                _ => CreatePhaseSmtCurve(transmitter, selection.Frequency)
            };
            curve.SmtMode = mode;
            curve.YAxisGroup = 0;
            curve.Color = AssignSmartColor(curves, transmitter, curve.YAxisGroup);
            curves.Add(curve);
        }
    }

    private static CurveDefinition CreateRatioCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.AmplitudeRatio, frequency, -1, "V/V", 1);
    }

    private static CurveDefinition CreateDifferenceCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.PhaseDifference, frequency, -1, "mdeg", 0);
    }

    private static CurveDefinition CreateAmplitudeCurve(int transmitter, int receiver, FrequencyBand frequency)
    {
        DataType type = receiver == 0 ? DataType.AmplitudeRx1 : DataType.AmplitudeRx2;
        return CreateCurve(transmitter, type, frequency, receiver, "mV", 1);
    }

    private static CurveDefinition CreateRhoSmtCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.RhoSmt, frequency, -1, "Ohm*m", 0);
    }

    private static CurveDefinition CreatePhaseSmtCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.PhaseSmt, frequency, -1, "mdeg", 0);
    }

    private static CurveDefinition CreateRhoAttSmtCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.RhoAttSmt, frequency, -1, "Ohm*m", 0);
    }

    private static CurveDefinition CreateAttDbSmtCurve(int transmitter, FrequencyBand frequency)
    {
        return CreateCurve(transmitter, DataType.AttDbSmt, frequency, -1, "dB", 0);
    }

    private static CurveDefinition CreateSilentAmplitudeCurve(int receiver, FrequencyBand frequency)
    {
        DataType type = receiver == 0 ? DataType.SilentAmplitudeRx1 : DataType.SilentAmplitudeRx2;
        return new CurveDefinition(
            GenerateCurveId(ElementType.SilentTx, receiver, type, frequency),
            GenerateDisplayName(ElementType.SilentTx, receiver, type),
            "mV",
            ElementType.SilentTx,
            receiver,
            type,
            frequency)
        {
            YAxisGroup = 1,
            Color = SilentColor
        };
    }

    private static CurveDefinition CreateSilentPhaseDifferenceCurve(FrequencyBand frequency)
    {
        return new CurveDefinition(
            GenerateCurveId(ElementType.SilentTx, 0, DataType.SilentPhaseDifference, frequency),
            GenerateDisplayName(ElementType.SilentTx, 0, DataType.SilentPhaseDifference),
            "mdeg",
            ElementType.SilentTx,
            0,
            DataType.SilentPhaseDifference,
            frequency)
        {
            YAxisGroup = 0,
            Color = SilentColor
        };
    }

    private static CurveDefinition CreateTemperatureCurve(FrequencyBand frequency)
    {
        return new CurveDefinition
        {
            Id = "TEMPERATURE",
            DisplayName = "Температура",
            Units = "degC",
            YAxisGroup = 0,
            Enabled = true,
            Color = TemperatureColor,
            SourceElement = ElementType.Transmitter,
            ElementIndex = 0,
            DataType = DataType.Temperature,
            Frequency = frequency
        };
    }

    private static CurveDefinition CreateCurve(int transmitter, DataType type, FrequencyBand frequency, int receiver, string units, int axis)
    {
        return new CurveDefinition(
            GenerateCurveId(ElementType.Transmitter, transmitter, type, frequency, receiver),
            GenerateDisplayName(ElementType.Transmitter, transmitter, type, receiver),
            units,
            ElementType.Transmitter,
            transmitter,
            type,
            frequency)
        {
            YAxisGroup = axis,
            Color = GetCurveColorForAxis(transmitter, axis)
        };
    }

    private static string GenerateCurveId(ElementType element, int elementIndex, DataType type, FrequencyBand frequency, int receiver = -1)
    {
        string elementToken = element switch
        {
            ElementType.Transmitter => $"T{elementIndex + 1}",
            ElementType.Receiver => $"R{elementIndex + 1}",
            ElementType.SilentTx => $"SILENT_R{elementIndex + 1}",
            _ => string.Empty
        };
        string receiverToken = receiver >= 0 ? $"_R{receiver + 1}" : string.Empty;
        return $"{elementToken}_{GetDataTypeDisplayName(type)}_{GetFrequencyDisplayName(frequency)}{receiverToken}";
    }

    private static string GenerateDisplayName(ElementType element, int elementIndex, DataType type, int receiver = -1)
    {
        string elementToken = element switch
        {
            ElementType.Transmitter => $"T{elementIndex + 1}",
            ElementType.Receiver => $"R{elementIndex + 1}",
            ElementType.SilentTx when type == DataType.SilentPhaseDifference => "Молчащие",
            ElementType.SilentTx => $"Молчащий R{elementIndex + 1}",
            _ => "Unknown"
        };
        string receiverToken = receiver >= 0 ? $" R{receiver + 1}" : string.Empty;
        return $"{elementToken} {GetDataTypeDisplayName(type)}{receiverToken}";
    }

    private static string GetDataTypeDisplayName(DataType type)
    {
        return type switch
        {
            DataType.AmplitudeRx1 or DataType.AmplitudeRx2 or DataType.SilentAmplitudeRx1 or DataType.SilentAmplitudeRx2 => "Амплитуда",
            DataType.PhaseSmt => "Фаза",
            DataType.AmplitudeRatio => "Отношение амплитуд",
            DataType.PhaseDifference or DataType.SilentPhaseDifference => "Разность фаз",
            DataType.RhoSmt => "УЭС",
            DataType.RhoAttSmt => "УЭС по затуханию",
            DataType.AttDbSmt => "Затухание",
            DataType.Temperature => "Температура",
            _ => string.Empty
        };
    }

    private static string GetFrequencyDisplayName(FrequencyBand frequency)
    {
        return frequency == FrequencyBand.Freq400KHz ? "400 кГц" : "2000 кГц";
    }

    private static void RefreshCurveColors(List<CurveDefinition> curves)
    {
        List<CurveDefinition> assigned = [];
        bool[] corporateUsed = new bool[SchematicSelection.UiTransmitterCount];
        foreach (CurveDefinition curve in curves)
        {
            if (curve.DataType == DataType.Temperature)
            {
                curve.Color = TemperatureColor;
            }
            else
            {
                int transmitter = curve.SourceElement == ElementType.Transmitter ? curve.ElementIndex : -1;
                if (transmitter >= 0 && transmitter < corporateUsed.Length && corporateUsed[transmitter])
                {
                    transmitter = -1;
                }

                curve.Color = AssignSmartColor(assigned, transmitter, curve.YAxisGroup);
                if (curve.SourceElement == ElementType.Transmitter && curve.ElementIndex >= 0 && curve.ElementIndex < corporateUsed.Length)
                {
                    corporateUsed[curve.ElementIndex] = true;
                }
            }

            assigned.Add(curve);
        }
    }

    private static int AssignSmartColor(IReadOnlyList<CurveDefinition> existing, int transmitter, int axis)
    {
        List<int> used = existing.Select(curve => curve.Color).ToList();
        bool hasPreferred = transmitter >= 0 && transmitter < SchematicSelection.UiTransmitterCount;
        int preferred = hasPreferred ? GetCurveColorForAxis(transmitter, axis) : 0;
        if (hasPreferred && !used.Contains(preferred))
        {
            return preferred;
        }

        if (used.Count == 0)
        {
            return ExtendedPalette[0];
        }

        List<int> candidates = [];
        void AddCandidate(int color)
        {
            if (!candidates.Contains(color))
            {
                candidates.Add(color);
            }
        }

        if (hasPreferred)
        {
            AddCandidate(preferred);
        }

        for (int index = 0; index < SchematicSelection.UiTransmitterCount; index++)
        {
            AddCandidate(LeftAxisColors[index]);
            AddCandidate(RightAxisColors[index]);
        }

        foreach (int color in ExtendedPalette)
        {
            AddCandidate(color);
        }

        for (int index = 0; index < 64; index++)
        {
            AddCandidate(GenerateAdaptivePaletteColor(index));
        }

        int bestColor = candidates[0];
        double bestScore = double.NegativeInfinity;
        foreach (int candidate in candidates)
        {
            double score = used.Min(color => ColorDistance(candidate, color));
            if (!used.Contains(candidate))
            {
                score += 10.0;
            }

            if (hasPreferred && candidate == preferred)
            {
                score += 18.0;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestColor = candidate;
            }
        }

        return bestColor;
    }

    private static int GetCurveColorForAxis(int transmitter, int axis)
    {
        if (transmitter < 0 || transmitter >= SchematicSelection.UiTransmitterCount)
        {
            return 0;
        }

        return axis == 0 ? LeftAxisColors[transmitter] : RightAxisColors[transmitter];
    }

    private static double ColorDistance(int first, int second)
    {
        double r1 = first & 0xFF;
        double g1 = (first >> 8) & 0xFF;
        double b1 = (first >> 16) & 0xFF;
        double r2 = second & 0xFF;
        double g2 = (second >> 8) & 0xFF;
        double b2 = (second >> 16) & 0xFF;
        double rMean = (r1 + r2) * 0.5;
        double dr = r1 - r2;
        double dg = g1 - g2;
        double db = b1 - b2;
        return Math.Sqrt((2.0 + rMean / 256.0) * dr * dr + 4.0 * dg * dg + (2.0 + (255.0 - rMean) / 256.0) * db * db);
    }

    private static int GenerateAdaptivePaletteColor(int index)
    {
        double[] saturations = [0.36, 0.42, 0.48];
        double[] lightnessValues = [0.36, 0.42, 0.47, 0.40];
        double hue = (29.0 + 137.50776405 * index) % 360.0;
        return HslToColorRef(hue, saturations[index % saturations.Length], lightnessValues[(index / 3) % lightnessValues.Length]);
    }

    private static int HslToColorRef(double hueDegrees, double saturation, double lightness)
    {
        double hue = (hueDegrees % 360.0) / 360.0;
        double r = lightness;
        double g = lightness;
        double b = lightness;
        if (saturation > 0.0)
        {
            double q = lightness < 0.5 ? lightness * (1.0 + saturation) : lightness + saturation - lightness * saturation;
            double p = 2.0 * lightness - q;
            r = HueToRgb(p, q, hue + 1.0 / 3.0);
            g = HueToRgb(p, q, hue);
            b = HueToRgb(p, q, hue - 1.0 / 3.0);
        }

        return Rgb((int)Math.Round(r * 255.0), (int)Math.Round(g * 255.0), (int)Math.Round(b * 255.0));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0) t += 1.0;
        if (t > 1.0) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    private static int Rgb(int red, int green, int blue)
    {
        return red | (green << 8) | (blue << 16);
    }
}
