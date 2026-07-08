using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Charting.Series;

public enum DataType
{
    PhaseDifference,
    AmplitudeRatio,
    AmplitudeRx1,
    AmplitudeRx2,
    PhaseSmt,
    RhoSmt,
    SilentAmplitudeRx1,
    SilentAmplitudeRx2,
    SilentPhaseDifference,
    Temperature
}

public enum FrequencyBand
{
    Freq400KHz,
    Freq2000KHz
}

public enum Transmitter
{
    T1 = 0,
    T2 = 1,
    T3 = 2,
    T4 = 3,
    T5 = 4
}

public enum ElementType
{
    Transmitter,
    Receiver,
    SilentTx
}

public enum XAxisType
{
    FrameNumber,
    Temperature
}

public enum SmtComputationMode
{
    FromSonde,
    FromComputer
}

public enum PresetType
{
    None,
    AllAmplitudesR1,
    AllAmplitudesR2,
    AmplitudeRatios,
    PhaseDifferences,
    SilentAmplitudes,
    SilentPhaseDifference,
    Temperature,
    RhoSmt,
    PhaseSmt,
    Custom
}

public readonly record struct DataIndexWindow(int StartIndex, int EndIndex)
{
    public int Count => Math.Max(0, EndIndex - StartIndex);
}

public sealed record ChartDataRequest(
    DataType Type,
    FrequencyBand Frequency,
    IReadOnlyList<bool>? EnabledTransmitters = null,
    double MinX = 0.0,
    double MaxX = -1.0,
    double MinY = -1e6,
    double MaxY = 1e6);

public sealed class CurveDefinition
{
    public const int SolidLineStyle = 0;
    public const int BlackColor = 0x00000000;
    public const int BlueColorRef = 0x00FF0000;

    public CurveDefinition()
    {
    }

    public CurveDefinition(
        string id,
        string displayName,
        string units,
        ElementType sourceElement,
        int elementIndex,
        DataType dataType,
        FrequencyBand frequency)
    {
        Id = id;
        DisplayName = displayName;
        Units = units;
        Enabled = true;
        Color = BlueColorRef;
        SourceElement = sourceElement;
        ElementIndex = elementIndex;
        DataType = dataType;
        Frequency = frequency;
    }

    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Units { get; set; } = string.Empty;

    public int YAxisGroup { get; set; }

    public bool Enabled { get; set; }

    public int Color { get; set; } = BlackColor;

    public int LineStyle { get; set; } = SolidLineStyle;

    public ElementType SourceElement { get; set; } = ElementType.Transmitter;

    public int ElementIndex { get; set; }

    public DataType DataType { get; set; } = DataType.AmplitudeRx1;

    public FrequencyBand Frequency { get; set; } = FrequencyBand.Freq400KHz;

    public SmtComputationMode SmtMode { get; set; } = SmtComputationMode.FromSonde;

    public float ValueScale { get; set; } = 1.0f;

    public CurveDefinition Copy()
    {
        return new CurveDefinition
        {
            Id = Id,
            DisplayName = DisplayName,
            Units = Units,
            YAxisGroup = YAxisGroup,
            Enabled = Enabled,
            Color = Color,
            LineStyle = LineStyle,
            SourceElement = SourceElement,
            ElementIndex = ElementIndex,
            DataType = DataType,
            Frequency = Frequency,
            SmtMode = SmtMode,
            ValueScale = ValueScale
        };
    }
}

public sealed class ChartConfiguration
{
    public List<CurveDefinition> Curves { get; } = [];

    public bool HasDualAxis { get; private set; }

    public string LeftAxisUnits { get; private set; } = string.Empty;

    public string RightAxisUnits { get; private set; } = string.Empty;

    public XAxisType XAxisType { get; set; } = XAxisType.FrameNumber;

    public PresetType ActivePreset { get; set; } = PresetType.None;

    public string PresetDisplayName { get; set; } = string.Empty;

    public bool IsEmpty => Curves.Count == 0;

    public bool IsPresetActive => ActivePreset is not PresetType.None and not PresetType.Custom;

    public void Clear()
    {
        Curves.Clear();
        HasDualAxis = false;
        LeftAxisUnits = string.Empty;
        RightAxisUnits = string.Empty;
        XAxisType = XAxisType.FrameNumber;
        ActivePreset = PresetType.None;
        PresetDisplayName = string.Empty;
    }

    public ChartConfiguration Copy()
    {
        ChartConfiguration copy = new()
        {
            XAxisType = XAxisType,
            ActivePreset = ActivePreset,
            PresetDisplayName = PresetDisplayName
        };

        foreach (CurveDefinition curve in Curves)
        {
            copy.Curves.Add(curve.Copy());
        }

        copy.UpdateAxisConfiguration();
        return copy;
    }

    public void AddCurve(CurveDefinition curve)
    {
        Curves.Add(curve);
        UpdateAxisConfiguration();
    }

    public void RemoveCurve(string curveId)
    {
        Curves.RemoveAll(curve => curve.Id == curveId);
        UpdateAxisConfiguration();
    }

    public void UpdateAxisConfiguration()
    {
        bool hasLeftAxis = false;
        bool hasRightAxis = false;
        string leftUnits = string.Empty;
        string rightUnits = string.Empty;

        foreach (CurveDefinition curve in Curves)
        {
            if (!curve.Enabled)
            {
                continue;
            }

            if (curve.YAxisGroup == 0)
            {
                hasLeftAxis = true;
                if (leftUnits.Length == 0)
                {
                    leftUnits = curve.Units;
                }
            }
            else if (curve.YAxisGroup == 1)
            {
                hasRightAxis = true;
                if (rightUnits.Length == 0)
                {
                    rightUnits = curve.Units;
                }
            }
        }

        HasDualAxis = hasLeftAxis && hasRightAxis;
        LeftAxisUnits = hasLeftAxis ? leftUnits : string.Empty;
        RightAxisUnits = hasRightAxis ? rightUnits : string.Empty;
    }
}

public sealed record ConfiguredChartData(
    List<List<float>> LeftAxisData,
    List<List<float>> RightAxisData);

public sealed record GroupedCurves(
    List<CurveDefinition> LeftAxisCurves,
    List<CurveDefinition> RightAxisCurves);
