namespace AMK.DataVisualization.App.Services;

internal readonly record struct ChartIndexRange(int StartIndex, int EndIndex);

/// <summary>
/// Which Y axis a drag-zoom acts on while the graph shows two axes. Legacy
/// exposes this as the "Zoom оси Y" combo and forces <see cref="Both"/> whenever
/// the configuration is single-axis.
/// </summary>
internal enum YZoomTarget
{
    Both,
    LeftOnly,
    RightOnly
}

/// <summary>
/// Y part of a drag-zoom gesture. Besides the selected ranges it carries the
/// range each axis is currently drawn from, so the axis that is not being zoomed
/// can be pinned to exactly what the user already sees instead of being
/// proportionally rescaled.
/// </summary>
internal readonly record struct YZoomRequest(
    YZoomTarget Target,
    (double Min, double Max)? LeftSelection,
    (double Min, double Max)? RightSelection,
    (double Min, double Max)? LeftAxisBaseRange,
    (double Min, double Max)? RightAxisBaseRange);

internal sealed class ChartViewportState
{
    private readonly List<ChartViewportSnapshot> zoomHistory = [];
    private readonly List<XAxisSnapshot> xZoomHistory = [];
    private readonly List<YAxisSnapshot> yZoomHistory = [];
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

    /// <summary>
    /// The per-axis undo buttons stay usable while the axis still has something to
    /// step back to: either a recorded step or a custom window that legacy returns
    /// to the full range.
    /// </summary>
    public bool CanUndoZoomX => !IsFlowEnabled && (xZoomHistory.Count > 0 || HasCustomXRange);

    public bool CanUndoZoomY => yZoomHistory.Count > 0 || LeftYRange is not null || RightYRange is not null;

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
        ClearAxisHistory();
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
        ClearAxisHistory();
        return true;
    }

    public bool TryScrollXTo(int recordCount, int requestedStartIndex)
    {
        if (recordCount <= 0 || IsFlowEnabled || !HasCustomXRange)
        {
            return false;
        }

        ChartIndexRange visible = GetVisibleRange(recordCount);
        int visibleCount = visible.EndIndex - visible.StartIndex + 1;
        if (visibleCount <= 0 || visibleCount >= recordCount)
        {
            return false;
        }

        int maximumStart = recordCount - visibleCount;
        int newStart = Math.Clamp(requestedStartIndex, 0, maximumStart);
        startIndex = newStart;
        endIndex = newStart + visibleCount - 1;
        IsToLatest = false;

        // A manual Y zoom survives horizontal navigation, as in legacy: the caller
        // clamps it into the extent of the newly visible records once they are
        // known. An automatic Y fit stays automatic and simply recalculates.
        return true;
    }

    /// <summary>
    /// Fits the manual Y windows to the extent of the records now visible: the
    /// window keeps its height and slides inside the extent, or adopts the extent
    /// when the extent is the smaller of the two. Ranges left on automatic stay
    /// automatic. Returns whether anything moved.
    /// </summary>
    public bool ClampYRangesToData(
        (double Min, double Max)? leftDataRange,
        (double Min, double Max)? rightDataRange)
    {
        bool changed = false;
        if (TryFitWindow(LeftYRange, leftDataRange, out (double Min, double Max)? fittedLeft))
        {
            LeftYRange = fittedLeft;
            changed = true;
        }

        if (TryFitWindow(RightYRange, rightDataRange, out (double Min, double Max)? fittedRight))
        {
            RightYRange = fittedRight;
            changed = true;
        }

        return changed;
    }

    private static bool TryFitWindow(
        (double Min, double Max)? window,
        (double Min, double Max)? dataRange,
        out (double Min, double Max)? fitted)
    {
        fitted = window;
        if (window is not { } view || dataRange is not { } data)
        {
            return false;
        }

        double height = view.Max - view.Min;
        double dataMin = Math.Min(data.Min, data.Max);
        double dataMax = Math.Max(data.Min, data.Max);
        if (!(height > 0) || !double.IsFinite(height) || !double.IsFinite(dataMax - dataMin))
        {
            return false;
        }

        (double Min, double Max) target = dataMax - dataMin <= height
            ? (dataMin, dataMax)
            : Slide(view.Min, height, dataMin, dataMax);
        if (Math.Abs(target.Min - view.Min) < 1e-9 && Math.Abs(target.Max - view.Max) < 1e-9)
        {
            return false;
        }

        fitted = target;
        return true;
    }

    private static (double Min, double Max) Slide(double currentMin, double height, double dataMin, double dataMax)
    {
        double min = Math.Clamp(currentMin, dataMin, dataMax - height);
        return (min, min + height);
    }

    public bool TrySetScrolledYRanges(
        (double Min, double Max)? leftYRange,
        (double Min, double Max)? rightYRange)
    {
        (double Min, double Max)? normalizedLeft = Normalize(leftYRange);
        (double Min, double Max)? normalizedRight = Normalize(rightYRange);
        bool changed = false;

        if (LeftYRange is not null && normalizedLeft is not null)
        {
            LeftYRange = normalizedLeft;
            changed = true;
        }

        if (RightYRange is not null && normalizedRight is not null)
        {
            RightYRange = normalizedRight;
            changed = true;
        }

        return changed;
    }

    public bool ApplyDragZoom(
        int recordCount,
        double startFraction,
        double endFraction,
        YZoomRequest yZoom)
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

        if (!TryResolveZoomedYRanges(
                yZoom,
                out (double Min, double Max)? resolvedLeft,
                out (double Min, double Max)? resolvedRight))
        {
            return false;
        }

        if (IsFlowEnabled && resolvedLeft is { } flowLeft && flowLeft.Max - flowLeft.Min < 10.0)
        {
            return false;
        }

        PushHistory();
        if (!IsFlowEnabled)
        {
            startIndex = selectedStart;
            endIndex = selectedEnd;
            HasCustomXRange = true;
            IsToLatest = false;
        }

        LeftYRange = resolvedLeft;
        RightYRange = resolvedRight;
        return true;
    }

    /// <summary>
    /// Turns the gesture into the ranges both axes end up with. For a targeted
    /// zoom the other axis keeps its stored range, or is pinned to the range it is
    /// currently drawn from, so it does not move while the target axis zooms.
    /// </summary>
    private bool TryResolveZoomedYRanges(
        YZoomRequest yZoom,
        out (double Min, double Max)? resolvedLeft,
        out (double Min, double Max)? resolvedRight)
    {
        switch (yZoom.Target)
        {
            case YZoomTarget.LeftOnly:
                resolvedLeft = Normalize(yZoom.LeftSelection);
                resolvedRight = RightYRange ?? Normalize(yZoom.RightAxisBaseRange);
                return resolvedLeft is not null;

            case YZoomTarget.RightOnly:
                resolvedRight = Normalize(yZoom.RightSelection);
                resolvedLeft = LeftYRange ?? Normalize(yZoom.LeftAxisBaseRange);
                return resolvedRight is not null;

            default:
                resolvedLeft = Normalize(yZoom.LeftSelection);
                resolvedRight = Normalize(yZoom.RightSelection);
                return resolvedLeft is not null || resolvedRight is not null;
        }
    }

    public bool TryUndoZoom()
    {
        if (zoomHistory.Count == 0)
        {
            return false;
        }

        ChartViewportSnapshot previous = zoomHistory[^1];
        zoomHistory.RemoveAt(zoomHistory.Count - 1);

        // The per-axis stacks are pushed together with the combined one, so a
        // combined undo consumes one step from each to keep them aligned.
        PopAxisHistories();

        startIndex = previous.StartIndex;
        endIndex = previous.EndIndex;
        HasCustomXRange = IsFlowEnabled ? false : previous.HasCustomXRange;
        IsToLatest = IsFlowEnabled ? false : previous.IsToLatest;
        LeftYRange = previous.LeftYRange;
        RightYRange = previous.RightYRange;
        return true;
    }

    /// <summary>
    /// Steps the X window back one zoom level without touching Y. An exhausted
    /// history returns X to the whole file, as legacy does.
    /// </summary>
    public bool TryUndoZoomX()
    {
        if (IsFlowEnabled)
        {
            return false;
        }

        if (xZoomHistory.Count > 0)
        {
            XAxisSnapshot previous = xZoomHistory[^1];
            xZoomHistory.RemoveAt(xZoomHistory.Count - 1);
            startIndex = previous.StartIndex;
            endIndex = previous.EndIndex;
            HasCustomXRange = previous.HasCustomXRange;
            IsToLatest = previous.IsToLatest;
            return true;
        }

        if (!HasCustomXRange)
        {
            return false;
        }

        HasCustomXRange = false;
        IsToLatest = false;
        return true;
    }

    /// <summary>
    /// Steps both Y ranges back one zoom level without touching the X window. An
    /// exhausted history drops back to the automatic Y fit.
    /// </summary>
    public bool TryUndoZoomY()
    {
        if (yZoomHistory.Count > 0)
        {
            YAxisSnapshot previous = yZoomHistory[^1];
            yZoomHistory.RemoveAt(yZoomHistory.Count - 1);
            LeftYRange = previous.LeftYRange;
            RightYRange = previous.RightYRange;
            return true;
        }

        if (LeftYRange is null && RightYRange is null)
        {
            return false;
        }

        LeftYRange = null;
        RightYRange = null;
        return true;
    }

    public void ResetRange()
    {
        HasCustomXRange = false;
        IsToLatest = false;
        LeftYRange = null;
        RightYRange = null;
        ClearAxisHistory();
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

    /// <summary>
    /// Records one zoom step on the combined stack and on both per-axis stacks, so
    /// the whole step and each axis alone can be undone independently.
    /// </summary>
    private void PushHistory()
    {
        zoomHistory.Add(new ChartViewportSnapshot(
            startIndex,
            endIndex,
            HasCustomXRange,
            IsToLatest,
            LeftYRange,
            RightYRange));
        xZoomHistory.Add(new XAxisSnapshot(startIndex, endIndex, HasCustomXRange, IsToLatest));
        yZoomHistory.Add(new YAxisSnapshot(LeftYRange, RightYRange));
    }

    private void PopAxisHistories()
    {
        if (xZoomHistory.Count > 0)
        {
            xZoomHistory.RemoveAt(xZoomHistory.Count - 1);
        }

        if (yZoomHistory.Count > 0)
        {
            yZoomHistory.RemoveAt(yZoomHistory.Count - 1);
        }
    }

    private void ClearAxisHistory()
    {
        xZoomHistory.Clear();
        yZoomHistory.Clear();
    }

    private readonly record struct ChartViewportSnapshot(
        int StartIndex,
        int EndIndex,
        bool HasCustomXRange,
        bool IsToLatest,
        (double Min, double Max)? LeftYRange,
        (double Min, double Max)? RightYRange);

    private readonly record struct XAxisSnapshot(
        int StartIndex,
        int EndIndex,
        bool HasCustomXRange,
        bool IsToLatest);

    private readonly record struct YAxisSnapshot(
        (double Min, double Max)? LeftYRange,
        (double Min, double Max)? RightYRange);
}
