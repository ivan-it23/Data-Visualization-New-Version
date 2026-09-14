using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Access;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;
using AMK.DataVisualization.Metrology;

namespace AMK.DataVisualization.Charting.Series;

public static class ChartDataCalculator
{
    public const float MissingValue = float.NaN;
    public const float MillidegreePerRadian = 57295.7795f;
    public const float AmplitudeRatioEpsilon = 1e-10f;

    public static ConfiguredChartData CalculateChartDataFromConfiguration(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartConfiguration configuration,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0)
        {
            return new ConfiguredChartData([], []);
        }

        return CalculateChartDataFromConfiguration(
            records,
            metadata,
            configuration,
            0.0,
            records.Count - 1,
            -1e6,
            1e6,
            metrology);
    }

    public static ConfiguredChartData CalculateChartDataFromConfiguration(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartConfiguration configuration,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0 || configuration.Curves.Count == 0)
        {
            return new ConfiguredChartData([], []);
        }

        return CalculateCustomCurveData(records, metadata, configuration.Curves, minX, maxX, minY, maxY, metrology);
    }

    public static ConfiguredChartData CalculateCustomCurveData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        IReadOnlyList<CurveDefinition> curves,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0)
        {
            return new ConfiguredChartData([], []);
        }

        return CalculateCustomCurveData(records, metadata, curves, 0.0, records.Count - 1, -1e6, 1e6, metrology);
    }

    public static ConfiguredChartData CalculateCustomCurveData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        IReadOnlyList<CurveDefinition> curves,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0 || curves.Count == 0)
        {
            return new ConfiguredChartData([], []);
        }

        GroupedCurves groupedCurves = GroupCurvesByAxis(curves);
        List<List<float>> leftAxisData = new(groupedCurves.LeftAxisCurves.Count);
        foreach (CurveDefinition curve in groupedCurves.LeftAxisCurves)
        {
            leftAxisData.Add(CalculateSingleCurveData(records, metadata, curve, minX, maxX, minY, maxY, metrology));
        }

        List<List<float>> rightAxisData = new(groupedCurves.RightAxisCurves.Count);
        foreach (CurveDefinition curve in groupedCurves.RightAxisCurves)
        {
            rightAxisData.Add(CalculateSingleCurveData(records, metadata, curve, minX, maxX, minY, maxY, metrology));
        }

        return new ConfiguredChartData(leftAxisData, rightAxisData);
    }

    public static GroupedCurves GroupCurvesByAxis(IReadOnlyList<CurveDefinition> curves)
    {
        List<CurveDefinition> leftAxisCurves = [];
        List<CurveDefinition> rightAxisCurves = [];

        foreach (CurveDefinition curve in curves)
        {
            if (!curve.Enabled)
            {
                continue;
            }

            if (curve.YAxisGroup == 0)
            {
                leftAxisCurves.Add(curve);
            }
            else if (curve.YAxisGroup == 1)
            {
                rightAxisCurves.Add(curve);
            }
        }

        return new GroupedCurves(leftAxisCurves, rightAxisCurves);
    }

    public static List<float> CalculateSingleCurveData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0)
        {
            return [];
        }

        return CalculateSingleCurveData(records, metadata, curve, 0.0, records.Count - 1, -1e6, 1e6, metrology);
    }

    public static List<float> CalculateSingleCurveData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology = null)
    {
        if (records.Count == 0)
        {
            return [];
        }

        List<float> output = curve.DataType switch
        {
            DataType.RhoSmt => CalculateFilteredRhoSmtData(records, metadata, curve, minX, maxX, minY, maxY, metrology),
            DataType.RhoAttSmt => CalculateFilteredRhoAttSmtData(records, metadata, curve, minX, maxX, minY, maxY, metrology),
            DataType.Temperature => CalculateFilteredTemperatureData(records, metadata, minX, maxX, minY, maxY),
            DataType.PreambleAccelerationX
                or DataType.PreambleAccelerationY
                or DataType.PreambleAccelerationZ
                or DataType.PreambleAccelerationMagnitude
                => CalculateFilteredPreambleData(records, metadata, curve.DataType, minX, maxX, minY, maxY),
            _ => CalculateAlignedSingleCurveData(records, metadata, curve, minX, maxX, minY, maxY, metrology)
        };

        if (curve.ValueScale != 1.0f)
        {
            for (int index = 0; index < output.Count; index++)
            {
                output[index] *= curve.ValueScale;
            }
        }

        return output;
    }

    public static List<List<float>> CalculateChartData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        DataType type,
        FrequencyBand frequency)
    {
        return CalculateChartData(records, metadata, new ChartDataRequest(
            type,
            frequency,
            EnabledTransmitters: null,
            MinX: 0.0,
            MaxX: records.Count - 1,
            MinY: -1e6,
            MaxY: 1e6));
    }

    public static List<List<float>> CalculateChartData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartDataRequest request)
    {
        List<List<float>> output = [];
        if (records.Count == 0)
        {
            return output;
        }

        DataIndexWindow window = GetDataIndexRange(records.Count, request.MinX, request.MaxX >= request.MinX ? request.MaxX : records.Count - 1);
        if (window.StartIndex >= window.EndIndex)
        {
            return output;
        }

        int frequencyIndex = request.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        switch (request.Type)
        {
            case DataType.PhaseDifference:
            case DataType.AmplitudeRatio:
            case DataType.AmplitudeRx1:
            case DataType.AmplitudeRx2:
            case DataType.PhaseSmt:
            case DataType.RhoSmt:
            case DataType.AttDbSmt:
            case DataType.RhoAttSmt:
                CalculateActiveTransmitterData(records, metadata, request, window, frequencyIndex, output);
                break;

            case DataType.SilentAmplitudeRx1:
            case DataType.SilentAmplitudeRx2:
            case DataType.SilentPhaseDifference:
                CalculateSilentData(records, metadata, request, window, frequencyIndex, output);
                break;

            case DataType.Temperature:
                CalculateTemperatureData(records, metadata, request, window, output);
                break;

            case DataType.PreambleAccelerationX:
            case DataType.PreambleAccelerationY:
            case DataType.PreambleAccelerationZ:
            case DataType.PreambleAccelerationMagnitude:
                CalculatePreambleData(records, metadata, request, window, output);
                break;
        }

        return output;
    }

    private static List<float> CalculateAlignedSingleCurveData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology)
    {
        if (curve.DataType == DataType.PhaseSmt && curve.SmtMode == SmtComputationMode.FromComputer)
        {
            return CalculateComputerPhaseSmtData(records, metadata, curve, minX, maxX, minY, maxY, metrology);
        }

        if (curve.DataType == DataType.AttDbSmt && curve.SmtMode == SmtComputationMode.FromComputer)
        {
            return CalculateComputerAttDbSmtData(records, metadata, curve, minX, maxX, minY, maxY, metrology);
        }

        if (curve.DataType is DataType.SilentAmplitudeRx1 or DataType.SilentAmplitudeRx2 or DataType.SilentPhaseDifference)
        {
            List<List<float>> silentData = CalculateChartData(records, metadata, new ChartDataRequest(
                curve.DataType,
                curve.Frequency,
                EnabledTransmitters: null,
                MinX: minX,
                MaxX: maxX,
                MinY: -1e30,
                MaxY: 1e30));
            return silentData.Count > 0 ? silentData[0] : [];
        }

        bool[] enabledTransmitters = new bool[Math.Max(BinaryLayoutConstants.MaxTransmitterCount, curve.ElementIndex + 1)];
        if (curve.ElementIndex >= 0)
        {
            enabledTransmitters[curve.ElementIndex] = true;
        }

        List<List<float>> transmitterData = CalculateChartData(records, metadata, new ChartDataRequest(
            curve.DataType,
            curve.Frequency,
            EnabledTransmitters: enabledTransmitters,
            MinX: minX,
            MaxX: maxX,
            MinY: -1e30,
            MaxY: 1e30));

        return curve.ElementIndex >= 0 && curve.ElementIndex < transmitterData.Count
            ? transmitterData[curve.ElementIndex]
            : [];
    }

    private static List<float> CalculateFilteredRhoSmtData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex || curve.ElementIndex < 0)
        {
            return [];
        }

        int frequencyIndex = curve.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            ToolDataAccessView view = measurement.CreateAccessView();
            bool hasValue = false;
            float value = 0.0f;
            if (curve.SmtMode == SmtComputationMode.FromComputer
                && metrology is { } activeMetrology)
            {
                hasValue = MetrologyCalculator.TryCalculateRhoSmt(
                    activeMetrology,
                    measurement,
                    frequencyIndex,
                    curve.ElementIndex,
                    out value);
            }

            if (!hasValue)
            {
                hasValue = view.TryGetRhoSmt(frequencyIndex, curve.ElementIndex, out value);
            }

            if (!hasValue)
            {
                continue;
            }

            if (value >= minY && value <= maxY)
            {
                output.Add(value);
            }
        }

        return output;
    }

    /// <summary>
    /// Амплитудное УЭС: расчёт приложением, если метрология прочитана, иначе
    /// значение, пришедшее от прибора. Устроено так же, как ветка фазового УЭС.
    /// </summary>
    private static List<float> CalculateFilteredRhoAttSmtData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex || curve.ElementIndex < 0)
        {
            return [];
        }

        int frequencyIndex = curve.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            ToolDataAccessView view = measurement.CreateAccessView();
            bool hasValue = false;
            float value = 0.0f;
            if (curve.SmtMode == SmtComputationMode.FromComputer
                && metrology is { } activeMetrology)
            {
                hasValue = MetrologyCalculator.TryCalculateRhoAttSmt(
                    activeMetrology,
                    measurement,
                    frequencyIndex,
                    curve.ElementIndex,
                    out value);
            }

            if (!hasValue)
            {
                hasValue = view.TryGetRhoAttSmt(frequencyIndex, curve.ElementIndex, out value);
            }

            if (!hasValue)
            {
                continue;
            }

            if (value >= minY && value <= maxY)
            {
                output.Add(value);
            }
        }

        return output;
    }

    /// <summary>
    /// Симметризованное затухание в расчёте приложения, с откатом на значение
    /// прибора.
    /// </summary>
    private static List<float> CalculateComputerAttDbSmtData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex || curve.ElementIndex < 0)
        {
            return [];
        }

        int frequencyIndex = curve.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            ToolDataAccessView view = measurement.CreateAccessView();
            bool hasValue = false;
            float value = 0.0f;
            if (metrology is { } activeMetrology)
            {
                hasValue = MetrologyCalculator.TryCalculateAttDbSmt(
                    activeMetrology,
                    measurement,
                    frequencyIndex,
                    curve.ElementIndex,
                    out value);
            }

            if (!hasValue)
            {
                hasValue = view.TryGetAttDbSmt(frequencyIndex, curve.ElementIndex, out value);
            }

            AppendAlignedFilteredPlotValue(output, hasValue, value, minY, maxY);
        }

        return output;
    }

    private static List<float> CalculateComputerPhaseSmtData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        CurveDefinition curve,
        double minX,
        double maxX,
        double minY,
        double maxY,
        MetrologyData? metrology)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex || curve.ElementIndex < 0)
        {
            return [];
        }

        int frequencyIndex = curve.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            ToolDataAccessView view = measurement.CreateAccessView();
            bool hasValue = false;
            float phase = 0.0f;
            if (metrology is { } activeMetrology)
            {
                hasValue = MetrologyCalculator.TryCalculatePhaseSmt(
                    activeMetrology,
                    measurement,
                    frequencyIndex,
                    curve.ElementIndex,
                    out phase);
            }

            if (!hasValue)
            {
                hasValue = view.TryGetPhaseSmt(frequencyIndex, curve.ElementIndex, out phase);
            }

            float value = hasValue
                ? ConvertNormalizedPhaseToMillidegrees(NormalizePhase(phase))
                : 0.0f;
            AppendAlignedFilteredPlotValue(output, hasValue, value, minY, maxY);
        }

        return output;
    }

    private static List<float> CalculateFilteredTemperatureData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex)
        {
            return [];
        }

        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            float value = measurement.Temperature;
            if (value >= minY && value <= maxY)
            {
                output.Add(value);
            }
        }

        return output;
    }

    /// <summary>
    /// Величина из служебного заголовка записи. Записи без заголовка дают
    /// разрыв кривой, а не ноль: нуля в этом месте прибор не измерял.
    /// </summary>
    private static bool TryGetPreambleValue(MeasurementView measurement, DataType type, out float value)
    {
        value = MissingValue;
        if (measurement.Metadata.Preamble is not DevPreamble preamble)
        {
            return false;
        }

        value = type switch
        {
            DataType.PreambleAccelerationX => preamble.X,
            DataType.PreambleAccelerationY => preamble.Y,
            DataType.PreambleAccelerationZ => preamble.Z,
            DataType.PreambleAccelerationMagnitude => preamble.Magnitude,
            _ => MissingValue
        };

        return float.IsFinite(value);
    }

    private static void CalculatePreambleData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartDataRequest request,
        DataIndexWindow window,
        List<List<float>> output)
    {
        output.Add(new List<float>(window.Count));
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            bool hasValue = TryGetPreambleValue(measurement, request.Type, out float value);
            AppendAlignedFilteredPlotValue(output[0], hasValue, value, request.MinY, request.MaxY);
        }
    }

    private static List<float> CalculateFilteredPreambleData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        DataType type,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        DataIndexWindow window = GetDataIndexRange(records.Count, minX, maxX);
        if (window.StartIndex >= window.EndIndex)
        {
            return [];
        }

        List<float> output = new(window.Count);
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            if (TryGetPreambleValue(measurement, type, out float value) && value >= minY && value <= maxY)
            {
                output.Add(value);
            }
        }

        return output;
    }

    public static DataIndexWindow GetDataIndexRange(int dataSize, double minX, double maxX)
    {
        if (dataSize <= 0)
        {
            return new DataIndexWindow(0, 0);
        }

        int startIndex = (int)Math.Max(0.0, minX);
        int endIndex = (int)Math.Min(dataSize, maxX + 1.0);
        startIndex = Math.Min(startIndex, dataSize);
        endIndex = Math.Min(Math.Max(0, endIndex), dataSize);

        return startIndex >= endIndex
            ? new DataIndexWindow(0, 0)
            : new DataIndexWindow(startIndex, endIndex);
    }

    public static float NormalizePhase(float phaseRadians)
    {
        const float pi = MathF.PI;
        const float twoPi = 2.0f * MathF.PI;

        if (!float.IsFinite(phaseRadians))
        {
            return 0.0f;
        }

        phaseRadians = (phaseRadians + pi) % twoPi;
        if (phaseRadians < 0.0f)
        {
            phaseRadians += twoPi;
        }

        phaseRadians -= pi;
        if (phaseRadians <= -pi)
        {
            phaseRadians += twoPi;
        }

        return phaseRadians;
    }

    public static float ConvertNormalizedPhaseToMillidegrees(float normalizedPhaseRadians)
    {
        return normalizedPhaseRadians * MillidegreePerRadian;
    }

    public static List<float> CalculateXAxisData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        XAxisType xAxisType)
    {
        List<float> output = new(records.Count);
        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            output.Add(xAxisType == XAxisType.Temperature
                ? CreateMeasurementView(records, metadata, recordIndex).Temperature
                : recordIndex);
        }

        return output;
    }

    private static void CalculateActiveTransmitterData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartDataRequest request,
        DataIndexWindow window,
        int frequencyIndex,
        List<List<float>> output)
    {
        int availableTransmitterCount = GetAvailableTransmitterCount(records, metadata);
        for (int index = 0; index < availableTransmitterCount; index++)
        {
            output.Add([]);
        }

        bool[] validTransmitters = new bool[availableTransmitterCount];
        for (int transmitterIndex = 0; transmitterIndex < availableTransmitterCount; transmitterIndex++)
        {
            validTransmitters[transmitterIndex] = request.EnabledTransmitters is null
                || transmitterIndex >= request.EnabledTransmitters.Count
                || request.EnabledTransmitters[transmitterIndex];
            if (validTransmitters[transmitterIndex])
            {
                output[transmitterIndex].Capacity = window.Count;
            }
        }

        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            ToolDataAccessView view = CreateAccessView(records, metadata, recordIndex);
            int transmitterCount = Math.Min(availableTransmitterCount, view.TransmitterCount);

            for (int transmitterIndex = 0; transmitterIndex < availableTransmitterCount; transmitterIndex++)
            {
                if (!validTransmitters[transmitterIndex])
                {
                    continue;
                }

                float value = 0.0f;
                bool hasValue = false;
                if (transmitterIndex < transmitterCount)
                {
                    hasValue = TryCalculateActiveValue(view, request.Type, frequencyIndex, transmitterIndex, out value);
                }

                AppendAlignedFilteredPlotValue(output[transmitterIndex], hasValue, value, request.MinY, request.MaxY);
            }
        }
    }

    private static bool TryCalculateActiveValue(ToolDataAccessView view, DataType type, int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0.0f;
        switch (type)
        {
            case DataType.PhaseDifference:
                if (!view.TryGetDeltaPhase(frequencyIndex, transmitterIndex, out float phaseDifference))
                {
                    return false;
                }

                value = ConvertNormalizedPhaseToMillidegrees(NormalizePhase(phaseDifference));
                return true;

            case DataType.AmplitudeRatio:
                if (!view.TryGetAmplitudeRx1(frequencyIndex, transmitterIndex, out float rx1)
                    || !view.TryGetAmplitudeRx2(frequencyIndex, transmitterIndex, out float rx2))
                {
                    return false;
                }

                value = MathF.Abs(rx1) > AmplitudeRatioEpsilon ? rx2 / rx1 : 0.0f;
                return true;

            case DataType.AmplitudeRx1:
                return view.TryGetAmplitudeRx1(frequencyIndex, transmitterIndex, out value);

            case DataType.AmplitudeRx2:
                return view.TryGetAmplitudeRx2(frequencyIndex, transmitterIndex, out value);

            case DataType.PhaseSmt:
                if (!view.TryGetPhaseSmt(frequencyIndex, transmitterIndex, out float phase))
                {
                    return false;
                }

                value = ConvertNormalizedPhaseToMillidegrees(NormalizePhase(phase));
                return true;

            case DataType.RhoSmt:
                return view.TryGetRhoSmt(frequencyIndex, transmitterIndex, out value);

            case DataType.AttDbSmt:
                return view.TryGetAttDbSmt(frequencyIndex, transmitterIndex, out value);

            case DataType.RhoAttSmt:
                return view.TryGetRhoAttSmt(frequencyIndex, transmitterIndex, out value);

            default:
                return false;
        }
    }

    private static void CalculateSilentData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartDataRequest request,
        DataIndexWindow window,
        int frequencyIndex,
        List<List<float>> output)
    {
        output.Add(new List<float>(window.Count));
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            ToolDataAccessView view = CreateAccessView(records, metadata, recordIndex);
            bool hasValue;
            float value = 0.0f;
            switch (request.Type)
            {
                case DataType.SilentAmplitudeRx1:
                    hasValue = view.TryGetSilentAmplitudeRx1(frequencyIndex, out value);
                    break;

                case DataType.SilentAmplitudeRx2:
                    hasValue = view.TryGetSilentAmplitudeRx2(frequencyIndex, out value);
                    break;

                case DataType.SilentPhaseDifference:
                    hasValue = view.TryGetSilentDeltaPhase(frequencyIndex, out float diff);
                    if (hasValue)
                    {
                        diff = NormalizePhase(diff);
                    }

                    value = ConvertNormalizedPhaseToMillidegrees(diff);
                    break;

                default:
                    hasValue = false;
                    break;
            }

            AppendAlignedFilteredPlotValue(output[0], hasValue, value, request.MinY, request.MaxY);
        }
    }

    private static void CalculateTemperatureData(
        IReadOnlyList<GpToolData> records,
        IReadOnlyList<ToolRecordMetadata> metadata,
        ChartDataRequest request,
        DataIndexWindow window,
        List<List<float>> output)
    {
        output.Add(new List<float>(window.Count));
        for (int recordIndex = window.StartIndex; recordIndex < window.EndIndex; recordIndex++)
        {
            MeasurementView measurement = CreateMeasurementView(records, metadata, recordIndex);
            float value = measurement.Temperature;
            AppendAlignedFilteredPlotValue(output[0], float.IsFinite(value), value, request.MinY, request.MaxY);
        }
    }

    private static void AppendAlignedFilteredPlotValue(List<float> output, bool hasValue, float value, double minY, double maxY)
    {
        bool isVisibleValue = hasValue && value >= minY && value <= maxY;
        output.Add(isVisibleValue ? value : MissingValue);
    }

    private static int GetAvailableTransmitterCount(IReadOnlyList<GpToolData> records, IReadOnlyList<ToolRecordMetadata> metadata)
    {
        int maxCount = 0;
        for (int index = 0; index < records.Count; index++)
        {
            ToolDataAccessView view = CreateAccessView(records, metadata, index);
            maxCount = Math.Max(maxCount, view.TransmitterCount);
        }

        return maxCount > 0 ? maxCount : BinaryLayoutConstants.MaxTransmitterCount;
    }

    private static MeasurementView CreateMeasurementView(IReadOnlyList<GpToolData> records, IReadOnlyList<ToolRecordMetadata> metadata, int index)
    {
        GpToolData record = records[index];
        ToolRecordMetadata recordMetadata = index < metadata.Count
            ? metadata[index]
            : BuildCanonicalMetadata(record);
        return new MeasurementView(record, recordMetadata);
    }

    private static ToolDataAccessView CreateAccessView(IReadOnlyList<GpToolData> records, IReadOnlyList<ToolRecordMetadata> metadata, int index)
    {
        return CreateMeasurementView(records, metadata, index).CreateAccessView();
    }

    private static ToolRecordMetadata BuildCanonicalMetadata(GpToolData record)
    {
        uint toolCode = ToolDataAdapterRegistry.DecodeToolCode(record.Signature);
        ToolDataKind kind = UniversalCanonicalAdapter.DetermineKindFromToolCode(toolCode);
        ToolCapabilities capabilities = UniversalCanonicalAdapter.BuildCapabilities(toolCode);
        return new ToolRecordMetadata(kind, capabilities);
    }
}
