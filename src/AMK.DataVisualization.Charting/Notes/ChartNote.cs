using AMK.DataVisualization.Charting.Series;

namespace AMK.DataVisualization.Charting.Notes;

public sealed class ChartNote
{
    public string NoteId { get; set; } = string.Empty;

    public ulong CreatedUtcMs { get; set; }

    public ulong ConfigKey { get; set; }

    public string ConfigSnapshot { get; set; } = string.Empty;

    public string PresetName { get; set; } = string.Empty;

    public XAxisType XAxisType { get; set; } = XAxisType.FrameNumber;

    public int XIndex { get; set; }

    public double YLeft { get; set; }

    public double YRight { get; set; }

    public bool HasYRight { get; set; }

    public int AnchorAxisGroup { get; set; }

    public string Text { get; set; } = string.Empty;

    public ChartNote Copy()
    {
        return new ChartNote
        {
            NoteId = NoteId,
            CreatedUtcMs = CreatedUtcMs,
            ConfigKey = ConfigKey,
            ConfigSnapshot = ConfigSnapshot,
            PresetName = PresetName,
            XAxisType = XAxisType,
            XIndex = XIndex,
            YLeft = YLeft,
            YRight = YRight,
            HasYRight = HasYRight,
            AnchorAxisGroup = AnchorAxisGroup,
            Text = Text
        };
    }
}

public readonly record struct SnapshotCurveItem(string CurveId, int YAxisGroup);
