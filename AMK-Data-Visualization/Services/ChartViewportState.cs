namespace AMK.DataVisualization.App.Services;

internal readonly record struct ChartIndexRange(int StartIndex, int EndIndex);

internal sealed class ChartViewportState
{
    private readonly List<ChartViewportSnapshot> zoomHistory = [];
    private int startIndex;
    private int endIndex;

    public bool IsFlowEnabled { get; private set; }
    public int FlowWindowSize { get; private set; } = 100;
    public bool HasCustomXRange { get; private set; }
    public bool IsToLatest { get; private set; }
    public (double Min, double Max)? LeftYRange { get; private set; }
    public (double Min, double Max)? RightYRange { get; private set; }

    public bool IsZoomed => IsFlowEnabled || HasCustomXRange || LeftYRange is not null || RightYRange is not null;

    public bool CanUndoZoom => zoomHistory.Count > 0;

    public ChartIndexRange GetVisibleRange(int recordCount)
    {
        if (recordCount <= 0)
        {
            return new ChartIndexRange(0, -1);
        }

        int last = recordCount - 1;
        if (IsFlowEnabled)
        {
            return new ChartIndexRange(Math.Max(0, recordCount - FlowWindowSize), last);
        }

        if (!HasCustomXRange)
        {
            return new ChartIndexRange(0, last);
        }

        int actualEnd = IsToLatest ? last : Math.Clamp(endIndex, 0, last);
        return new ChartIndexRange(Math.Clamp(startIndex, 0, actualEnd), actualEnd);
    }

    public bool TryApplyXRange(int recordCount, string fromText, string toText, out string error)
    {
        error = string.Empty;
        if (recordCount <= 0)
        {
            error = "Нет данных для выбора диапазона";
            return false;
        }

        if (IsFlowEnabled)
        {
            error = "Диапазон X заблокирован, пока включён режим «Поток»";
            return false;
        }

        if (!TryParseIndex(fromText, 0, out int from) ||
            !TryParseIndex(toText, recordCount - 1, out int to))
        {
            error = "Введите целые номера записей";
            return false;
        }

        if (from < 0 || to >= recordCount || from >= to)
        {
            error = $"Диапазон должен находиться в пределах 0–{recordCount - 1}";
            return false;
        }

        if (to - from < 10)
        {
            error = "Минимальный диапазон X — 10 записей";
            return false;
        }

        startIndex = from;
        endIndex = to;
        zoomHistory.Clear();
        HasCustomXRange = true;
        IsToLatest = string.IsNullOrWhiteSpace(toText);
        LeftYRange = null;
        RightYRange = null;
        return true;
    }

    public bool TrySetFlow(bool enabled, string windowText, int recordCount, out string error)
    {
        error = string.Empty;
        if (enabled && recordCount <= 0)
        {
            error = "Нет данных для режима «Поток»";
            return false;
        }

        int windowSize = 100;
        if (!string.IsNullOrWhiteSpace(windowText) &&
            (!int.TryParse(windowText, out windowSize) || windowSize <= 0))
        {
            windowSize = 100;
        }

        FlowWindowSize = Math.Max(1, windowSize);
        IsFlowEnabled = enabled;
        HasCustomXRange = false;
        IsToLatest = false;
        LeftYRange = null;
        RightYRange = null;
        return true;
    }

    public bool ApplyDragZoom(
        int recordCount,
        double startFraction,
        double endFraction,
        (double Min, double Max)? leftYRange,
        (double Min, double Max)? rightYRange)
    {
        ChartIndexRange visible = GetVisibleRange(recordCount);
        int selectedStart = visible.StartIndex;
        int selectedEnd = visible.EndIndex;
        if (!IsFlowEnabled && visible.EndIndex >= visible.StartIndex)
        {
            int span = visible.EndIndex - visible.StartIndex;
            selectedStart = visible.StartIndex + (int)Math.Round(Math.Min(startFraction, endFraction) * span);
            selectedEnd = visible.StartIndex + (int)Math.Round(Math.Max(startFraction, endFraction) * span);
            if (selectedEnd - selectedStart < 10)
            {
                return false;
            }
        }

        (double Min, double Max)? normalizedLeft = Normalize(leftYRange);
        (double Min, double Max)? normalizedRight = Normalize(rightYRange);
        if (normalizedLeft is null && normalizedRight is null)
        {
            return false;
        }

        if (IsFlowEnabled && normalizedLeft is { } flowLeft && flowLeft.Max - flowLeft.Min < 10.0)
        {
            return false;
        }

        zoomHistory.Add(CaptureSnapshot());
        if (!IsFlowEnabled)
        {
            startIndex = selectedStart;
            endIndex = selectedEnd;
            HasCustomXRange = true;
            IsToLatest = false;
        }

        LeftYRange = normalizedLeft;
        RightYRange = normalizedRight;
        return true;
    }

    public bool TryUndoZoom()
    {
        if (zoomHistory.Count == 0)
        {
            return false;
        }

        ChartViewportSnapshot previous = zoomHistory[^1];
        zoomHistory.RemoveAt(zoomHistory.Count - 1);
        startIndex = previous.StartIndex;
        endIndex = previous.EndIndex;
        HasCustomXRange = IsFlowEnabled ? false : previous.HasCustomXRange;
        IsToLatest = IsFlowEnabled ? false : previous.IsToLatest;
        LeftYRange = previous.LeftYRange;
        RightYRange = previous.RightYRange;
        return true;
    }

    public void ResetRange()
    {
        HasCustomXRange = false;
        IsToLatest = false;
        LeftYRange = null;
        RightYRange = null;
    }

    public void ResetZoom()
    {
        ResetRange();
        zoomHistory.Clear();
    }

    public void ResetAll()
    {
        IsFlowEnabled = false;
        FlowWindowSize = 100;
        ResetRange();
        zoomHistory.Clear();
    }

    public bool TryResolveOperationRange(int recordCount, string fromText, string toText, out ChartIndexRange range)
    {
        range = new ChartIndexRange(0, -1);
        if (recordCount <= 0)
        {
            return false;
        }

        if (IsZoomed)
        {
            range = GetVisibleRange(recordCount);
            return true;
        }

        if (!TryParseIndex(fromText, 0, out int from) ||
            !TryParseIndex(toText, recordCount - 1, out int to))
        {
            return false;
        }

        from = Math.Clamp(from, 0, recordCount - 1);
        to = Math.Clamp(to, 0, recordCount - 1);
        range = new ChartIndexRange(from, to);
        return from <= to;
    }

    private static bool TryParseIndex(string text, int emptyValue, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = emptyValue;
            return true;
        }

        return int.TryParse(text.Trim(), out value);
    }

    private static (double Min, double Max)? Normalize((double Min, double Max)? range)
    {
        if (range is not { } value || !double.IsFinite(value.Min) || !double.IsFinite(value.Max))
        {
            return null;
        }

        double min = Math.Min(value.Min, value.Max);
        double max = Math.Max(value.Min, value.Max);
        return max - min > 0.000001 ? (min, max) : null;
    }

    private ChartViewportSnapshot CaptureSnapshot()
    {
        return new ChartViewportSnapshot(
            startIndex,
            endIndex,
            HasCustomXRange,
            IsToLatest,
            LeftYRange,
            RightYRange);
    }

    private readonly record struct ChartViewportSnapshot(
        int StartIndex,
        int EndIndex,
        bool HasCustomXRange,
        bool IsToLatest,
        (double Min, double Max)? LeftYRange,
        (double Min, double Max)? RightYRange);
}
