using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using Windows.Foundation;
using Microsoft.UI.Xaml.Input;
using Windows.UI;

namespace AMK.DataVisualization.App.Controls;

public sealed record ChartRenderSeries(
    IReadOnlyList<float> Values,
    string DisplayName,
    string LegendLabel,
    string LegendCategory,
    string Units,
    int ColorRef,
    int YAxisGroup);

public sealed record ChartZoomRequestedEventArgs(
    double StartFraction,
    double EndFraction,
    (double Min, double Max)? LeftYRange,
    (double Min, double Max)? RightYRange);

public sealed record ChartXScrollRequestedEventArgs(int StartIndex);

public sealed record ChartYScrollRequestedEventArgs(
    (double Min, double Max)? LeftYRange,
    (double Min, double Max)? RightYRange);

public sealed record ChartNoteMarker(
    string NoteId,
    float XValue,
    double YLeft,
    double YRight,
    bool HasYRight,
    int AnchorAxisGroup,
    string Text);

public sealed record ChartPointSelectedEventArgs(
    int SourceIndex,
    double YLeft,
    double YRight,
    bool HasRightAxis);

public sealed record ChartNoteMarkerEventArgs(string NoteId);

public sealed record ChartContextRequestedEventArgs(Point Position, bool HasCrosshair);

public sealed partial class LineChartControl : UserControl
{
    private const double HorizontalScrollBarBottomMargin = 4.0;
    private readonly List<ChartRenderSeries> series = [];
    private string title = string.Empty;
    private string leftAxisUnits = string.Empty;
    private string rightAxisUnits = string.Empty;
    private IReadOnlyList<float> xValues = [];
    private IReadOnlyList<float> segmentBoundaryXValues = [];
    private IReadOnlyList<ChartNoteMarker> noteMarkers = [];
    private readonly List<RenderedNoteMarker> renderedNoteMarkers = [];
    private string xAxisTitle = "Номер кадра";
    private string presetDisplayName = string.Empty;
    private (double Min, double Max)? requestedLeftRange;
    private (double Min, double Max)? requestedRightRange;
    private Rect currentPlot;
    private AxisScale? currentXScale;
    private AxisScale? currentLeftScale;
    private AxisScale? currentRightScale;
    private int visibleStartIndex;
    private CrosshairState? crosshair;
    private Point? dragStart;
    private Rectangle? selectionRectangle;
    private AxisScrollContext? leftYScrollContext;
    private AxisScrollContext? rightYScrollContext;
    private bool updatingScrollBars;
    private readonly Button displaySettingsButton;
    private readonly Button curveVisibilityButton;
    private readonly StackPanel leftOverlayHost;
    private readonly StackPanel centerOverlayHost;

    /// <summary>
    /// Horizontal host placed immediately to the right of the gear button, on the
    /// title line. The host fills it with the preset and frequency dropdowns.
    /// </summary>
    public Panel LeftOverlayHost => leftOverlayHost;

    /// <summary>
    /// Horizontal host centred on the title line for the compact schematic.
    /// </summary>
    public Panel CenterOverlayHost => centerOverlayHost;

    /// <summary>
    /// Shows the "eye" toggle at the end of the legend. Set by the host when at
    /// least one on-chart curve-selection overlay (preset dropdown or top
    /// schematic) is enabled for this graph.
    /// </summary>
    public bool ShowCurveVisibilityToggle { get; set; }

    /// <summary>
    /// Minimum Y where the legend starts, so the host can reserve room above it
    /// for the top-of-chart overlay (preset dropdown / schematic). Defaults to
    /// the historical legend top.
    /// </summary>
    public double LegendTopReserve { get; set; }

    public event EventHandler<FrameworkElement>? DisplaySettingsRequested;

    public event EventHandler<FrameworkElement>? CurveVisibilityRequested;

    public event EventHandler<ChartZoomRequestedEventArgs>? ZoomRequested;

    public event EventHandler<ChartXScrollRequestedEventArgs>? HorizontalScrollRequested;

    public event EventHandler<ChartYScrollRequestedEventArgs>? VerticalScrollRequested;

    public event EventHandler<ChartPointSelectedEventArgs>? PointSelected;

    public event EventHandler<ChartNoteMarkerEventArgs>? NoteMarkerActivated;

    public event EventHandler<ChartContextRequestedEventArgs>? ChartContextRequested;

    public bool IsFlowMode { get; set; }

    public bool HasCrosshair => crosshair is not null;
    private string emptyStateMessage = "Нет данных графика";

    public LineChartControl()
    {
        InitializeComponent();
        displaySettingsButton = CreateOverlayIconButton(
            "",
            "Настройка отображения кривых",
            22,
            20);
        displaySettingsButton.Click += (_, _) => DisplaySettingsRequested?.Invoke(this, displaySettingsButton);
        curveVisibilityButton = CreateOverlayIconButton(
            "",
            "Показать или скрыть кривые",
            24,
            18);
        curveVisibilityButton.Click += (_, _) => CurveVisibilityRequested?.Invoke(this, curveVisibilityButton);
        curveVisibilityButton.Visibility = Visibility.Collapsed;
        leftOverlayHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = null };
        centerOverlayHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = null };
        OverlayCanvas.Children.Add(displaySettingsButton);
        OverlayCanvas.Children.Add(leftOverlayHost);
        OverlayCanvas.Children.Add(centerOverlayHost);
        OverlayCanvas.Children.Add(curveVisibilityButton);
    }

    private static Button CreateOverlayIconButton(string glyph, string tooltip, double width, double height)
    {
        Button button = new()
        {
            Width = width,
            Height = height,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Glyph = glyph
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        Canvas.SetZIndex(button, 90);
        return button;
    }

    public void SetSeries(IReadOnlyList<float> sourceValues, string seriesTitle, string seriesUnits)
    {
        SetSeries(
            [new ChartRenderSeries(sourceValues, seriesTitle, seriesTitle, string.Empty, seriesUnits, ToColorRef(GetAccentColor()), 0)],
            seriesTitle,
            seriesUnits,
            string.Empty);
    }

    public void SetSeries(
        IReadOnlyList<ChartRenderSeries> sourceSeries,
        string chartTitle,
        string leftUnits,
        string rightUnits)
    {
        SetSeries(sourceSeries, chartTitle, leftUnits, rightUnits, [], "Номер кадра", string.Empty);
    }

    public void SetSeries(
        IReadOnlyList<ChartRenderSeries> sourceSeries,
        string chartTitle,
        string leftUnits,
        string rightUnits,
        IReadOnlyList<float> sourceXValues,
        string horizontalAxisTitle,
        string activePresetDisplayName,
        (double Min, double Max)? leftDisplayRange = null,
        (double Min, double Max)? rightDisplayRange = null,
        IReadOnlyList<float>? sourceSegmentBoundaryXValues = null,
        IReadOnlyList<ChartNoteMarker>? sourceNoteMarkers = null,
        int sourceStartIndex = 0)
    {
        series.Clear();
        series.AddRange(sourceSeries);
        title = chartTitle;
        leftAxisUnits = leftUnits;
        rightAxisUnits = rightUnits;
        xValues = sourceXValues;
        xAxisTitle = horizontalAxisTitle;
        presetDisplayName = activePresetDisplayName;
        requestedLeftRange = leftDisplayRange;
        requestedRightRange = rightDisplayRange;
        segmentBoundaryXValues = sourceSegmentBoundaryXValues ?? [];
        noteMarkers = sourceNoteMarkers ?? [];
        visibleStartIndex = sourceStartIndex;
        emptyStateMessage = "Нет данных графика";
        Render();
    }

    public void ClearCrosshair()
    {
        crosshair = null;
        Render();
    }

    public void ConfigureScrollBars(
        int totalRecordCount,
        int visibleStartIndex,
        int visibleEndIndex,
        bool showHorizontal)
    {
        updatingScrollBars = true;

        int visibleCount = visibleEndIndex >= visibleStartIndex
            ? visibleEndIndex - visibleStartIndex + 1
            : 0;
        int maximumStart = Math.Max(0, totalRecordCount - visibleCount);
        bool horizontalVisible = showHorizontal && visibleCount > 0 && maximumStart > 0;
        if (horizontalVisible)
        {
            HorizontalAxisScrollBar.Minimum = 0;
            HorizontalAxisScrollBar.Maximum = maximumStart;
            HorizontalAxisScrollBar.ViewportSize = visibleCount;
            HorizontalAxisScrollBar.SmallChange = Math.Max(1, Math.Round(maximumStart / 100.0));
            HorizontalAxisScrollBar.LargeChange = Math.Max(1, visibleCount);
            HorizontalAxisScrollBar.Value = Math.Clamp(visibleStartIndex, 0, maximumStart);
            SetScrollBarVisibility(HorizontalAxisScrollBar, true);
        }
        else
        {
            SetScrollBarVisibility(HorizontalAxisScrollBar, false);
        }

        ConfigureVerticalScrollBar();
        updatingScrollBars = false;
        UpdateScrollBarLayout(currentPlot, ActualWidth, ActualHeight);
    }

    public void ClearSeries(string emptyTitle = "Файл не загружен")
    {
        series.Clear();
        title = emptyTitle;
        leftAxisUnits = string.Empty;
        rightAxisUnits = string.Empty;
        xValues = [];
        segmentBoundaryXValues = [];
        noteMarkers = [];
        renderedNoteMarkers.Clear();
        xAxisTitle = "Номер кадра";
        presetDisplayName = string.Empty;
        requestedLeftRange = null;
        requestedRightRange = null;
        emptyStateMessage = "Нет данных графика";
        HideScrollBars();
        Render();
    }

    public void ShowLegacyEmptyState(string chartTitle, string message)
    {
        series.Clear();
        title = chartTitle;
        leftAxisUnits = string.Empty;
        rightAxisUnits = string.Empty;
        xValues = [];
        segmentBoundaryXValues = [];
        noteMarkers = [];
        renderedNoteMarkers.Clear();
        xAxisTitle = "Номер кадра";
        presetDisplayName = string.Empty;
        requestedLeftRange = null;
        requestedRightRange = null;
        emptyStateMessage = message;
        HideScrollBars();
        Render();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Render();
    }

    private void Render()
    {
        ChartCanvas.Children.Clear();
        renderedNoteMarkers.Clear();

        double width = ChartCanvas.ActualWidth;
        double height = ChartCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawTitle(width);
        double legendBottom = DrawLegend(width);
        bool hasRightAxis = series.Any(item => item.YAxisGroup == 1 && item.Values.Any(float.IsFinite));
        Thickness plotPadding = new(92, Math.Max(42, legendBottom + 8), hasRightAxis ? 92 : 32, 48);
        Rect plot = new(
            plotPadding.Left,
            plotPadding.Top,
            Math.Max(1, width - plotPadding.Left - plotPadding.Right),
            Math.Max(1, height - plotPadding.Top - plotPadding.Bottom));
        currentPlot = plot;
        UpdateScrollBarLayout(plot, width, height);

        List<float> allFinite = series.SelectMany(item => item.Values).Where(float.IsFinite).ToList();
        if (allFinite.Count == 0)
        {
            currentXScale = null;
            currentLeftScale = null;
            currentRightScale = null;
            DrawPlotFrame(plot, hasRightAxis);
            DrawEmptyGrid(plot);
            DrawEmptyState(plot);
            return;
        }

        (double Min, double Max)? leftRange = requestedLeftRange is { } requestedLeft
            ? ApplyDisplayPadding(requestedLeft)
            : GetAxisRange(0);
        (double Min, double Max)? rightRange = requestedRightRange is { } requestedRight
            ? ApplyDisplayPadding(requestedRight)
            : GetAxisRange(1);
        int pointCount = series.Count == 0 ? 0 : series.Max(item => item.Values.Count);
        (float Min, float Max) xRange = GetXRange(pointCount);
        bool isFrameAxis = string.Equals(xAxisTitle, "Номер кадра", StringComparison.Ordinal);
        AxisScale? leftScale = leftRange is { } left
            ? CreateAxisScale(left.Min, left.Max, Math.Max(2, (int)(plot.Height / 55)), includeZero: false, integerStep: false, expandBounds: false)
            : null;
        AxisScale? rightScale = rightRange is { } right
            ? CreateAxisScale(right.Min, right.Max, Math.Max(2, (int)(plot.Height / 55)), includeZero: false, integerStep: false, expandBounds: false)
            : null;
        currentLeftScale = leftScale;
        currentRightScale = rightScale;
        AxisScale xScale = CreateAxisScale(
            xRange.Min,
            xRange.Max,
            Math.Max(2, (int)(plot.Width / 100)),
            includeZero: false,
            integerStep: isFrameAxis,
            expandBounds: false);
        currentXScale = xScale;

        DrawPlotFrame(plot, hasRightAxis);
        DrawGrid(plot, leftScale, xScale);
        if (leftScale is { } leftAxis)
        {
            DrawLeftYAxisLabels(plot, leftAxis, leftAxisUnits);
        }

        if (rightScale is { } rightAxis)
        {
            DrawRightYAxisLabels(plot, rightAxis, rightAxisUnits);
        }

        DrawXAxisLabels(plot, xScale, isFrameAxis);

        foreach (ChartRenderSeries item in series)
        {
            AxisScale? scale = item.YAxisGroup == 1 ? rightScale : leftScale;
            if (scale is { } valueScale)
            {
                DrawSeries(plot, item, valueScale.Min, valueScale.Max, xScale.Min, xScale.Max);
            }
        }

        DrawSegmentBoundaries(plot, xScale);
        DrawNoteMarkers(plot, xScale, leftScale, rightScale);
        DrawCrosshair(plot);
    }

    private void DrawSegmentBoundaries(Rect plot, AxisScale xScale)
    {
        double range = xScale.Max - xScale.Min;
        if (segmentBoundaryXValues.Count == 0 || range <= 0)
        {
            return;
        }

        SolidColorBrush boundaryBrush = new(Colors.Black) { Opacity = 0.72 };
        foreach (float boundaryValue in segmentBoundaryXValues.Where(float.IsFinite))
        {
            if (boundaryValue < xScale.Min || boundaryValue > xScale.Max)
            {
                continue;
            }

            double x = plot.Left + ((boundaryValue - xScale.Min) / range * plot.Width);
            Line line = new()
            {
                X1 = x,
                X2 = x,
                Y1 = plot.Top,
                Y2 = plot.Bottom,
                Stroke = boundaryBrush,
                StrokeThickness = 1.4,
                StrokeDashArray = new DoubleCollection { 5, 4 },
                IsHitTestVisible = false
            };
            ChartCanvas.Children.Add(line);
        }
    }

    private void DrawNoteMarkers(
        Rect plot,
        AxisScale xScale,
        AxisScale? leftScale,
        AxisScale? rightScale)
    {
        if (noteMarkers.Count == 0 || xScale.Max <= xScale.Min)
        {
            return;
        }

        List<NoteLabelCandidate> labelCandidates = [];
        foreach (ChartNoteMarker marker in noteMarkers)
        {
            if (!float.IsFinite(marker.XValue)
                || marker.XValue < xScale.Min
                || marker.XValue > xScale.Max)
            {
                continue;
            }

            bool preferredRightAxis = marker.HasYRight
                && marker.AnchorAxisGroup == 1
                && rightScale is not null;
            if (!TryResolveNoteMarkerY(marker, preferredRightAxis, plot, leftScale, rightScale, out double y)
                && (!marker.HasYRight
                    || !TryResolveNoteMarkerY(marker, !preferredRightAxis, plot, leftScale, rightScale, out y)))
            {
                continue;
            }

            double x = MapX(plot, marker.XValue, xScale.Min, xScale.Max);
            if (x < plot.Left || x > plot.Right || y < plot.Top || y > plot.Bottom)
            {
                continue;
            }

            Ellipse point = new()
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Colors.Black),
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(point, x - 3);
            Canvas.SetTop(point, y - 3);
            ChartCanvas.Children.Add(point);
            renderedNoteMarkers.Add(new RenderedNoteMarker(marker.NoteId, x, y));

            if (labelCandidates.Count < 8)
            {
                labelCandidates.Add(new NoteLabelCandidate(x, y, BuildNotePreviewText(marker.Text)));
            }
        }

        DrawNoteLabels(plot, labelCandidates);
    }

    private static bool TryResolveNoteMarkerY(
        ChartNoteMarker marker,
        bool useRightAxis,
        Rect plot,
        AxisScale? leftScale,
        AxisScale? rightScale,
        out double y)
    {
        y = 0;
        AxisScale? selectedScale = useRightAxis && marker.HasYRight ? rightScale : leftScale;
        if (selectedScale is not { } scale || !(scale.Max > scale.Min))
        {
            return false;
        }

        double value = useRightAxis && marker.HasYRight ? marker.YRight : marker.YLeft;
        y = MapY(plot, value, scale.Min, scale.Max);
        return y >= plot.Top && y <= plot.Bottom;
    }

    private void DrawNoteLabels(Rect plot, IReadOnlyList<NoteLabelCandidate> candidates)
    {
        const double labelWidth = 130;
        const double labelHeight = 18;
        const double offsetX = 7;
        const double offsetY = 8;
        List<Rect> drawnLabelRects = [];

        foreach (NoteLabelCandidate candidate in candidates)
        {
            Rect labelRect = new(
                candidate.AnchorX + offsetX,
                candidate.AnchorY - offsetY - labelHeight,
                labelWidth,
                labelHeight);
            if (labelRect.Top < plot.Top)
            {
                labelRect.Y = candidate.AnchorY + offsetY;
            }

            if (labelRect.Right > plot.Right)
            {
                labelRect.X -= labelRect.Right - plot.Right;
            }

            if (labelRect.Left < plot.Left)
            {
                labelRect.X += plot.Left - labelRect.Left;
            }

            if (labelRect.Bottom > plot.Bottom)
            {
                labelRect.Y -= labelRect.Bottom - plot.Bottom;
            }

            bool placed = false;
            for (int attempt = 0; attempt < 4 && !placed; attempt++)
            {
                Rect testRect = labelRect;
                if (attempt > 0)
                {
                    testRect.Y += attempt * (labelHeight + 2);
                    if (testRect.Bottom > plot.Bottom)
                    {
                        continue;
                    }
                }

                if (drawnLabelRects.Any(existing => RectanglesOverlap(existing, testRect)))
                {
                    continue;
                }

                labelRect = testRect;
                placed = true;
            }

            if (!placed)
            {
                continue;
            }

            Border label = new()
            {
                Width = labelWidth,
                Height = labelHeight,
                Padding = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 240)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 145, 145, 145)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = candidate.Text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(label, labelRect.Left);
            Canvas.SetTop(label, labelRect.Top);
            ChartCanvas.Children.Add(label);
            drawnLabelRects.Add(labelRect);
        }
    }

    private void DrawCrosshair(Rect plot)
    {
        if (crosshair is not { } value || !plot.Contains(value.Position))
        {
            return;
        }

        SolidColorBrush brush = new(Colors.Red);
        AddAxisLine(value.Position.X, plot.Top, value.Position.X, plot.Bottom, brush);
        AddAxisLine(plot.Left, value.Position.Y, plot.Right, value.Position.Y, brush);
    }

    private static string BuildNotePreviewText(string source)
    {
        const int maxWords = 6;
        const int maxCharacters = 32;
        string normalized = source
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        string[] words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string result = string.Join(" ", words.Take(maxWords));
        if (words.Length > maxWords)
        {
            result += "...";
        }

        if (result.Length == 0)
        {
            return "(пусто)";
        }

        return result.Length > maxCharacters
            ? result[..(maxCharacters - 3)] + "..."
            : result;
    }

    private static bool RectanglesOverlap(Rect left, Rect right)
    {
        return left.Left < right.Right
            && left.Right > right.Left
            && left.Top < right.Bottom
            && left.Bottom > right.Top;
    }

    private void DrawPlotFrame(Rect plot, bool hasRightAxis)
    {
        SolidColorBrush axisBrush = new(Colors.Gray) { Opacity = 0.62 };

        AddAxisLine(plot.Left, plot.Top, plot.Left, plot.Bottom, axisBrush);
        AddAxisLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom, axisBrush);
        if (hasRightAxis)
        {
            AddAxisLine(plot.Right, plot.Top, plot.Right, plot.Bottom, axisBrush);
        }
    }

    private void DrawGrid(Rect plot, AxisScale? leftScale, AxisScale xScale)
    {
        SolidColorBrush gridBrush = new(Colors.Gray) { Opacity = 0.28 };
        if (leftScale is { } yScale)
        {
            foreach (double tick in EnumerateTicks(yScale))
            {
                double y = MapY(plot, tick, yScale.Min, yScale.Max);
                AddAxisLine(plot.Left, y, plot.Right, y, gridBrush);
            }
        }

        foreach (double tick in EnumerateTicks(xScale))
        {
            double x = MapX(plot, tick, xScale.Min, xScale.Max);
            AddAxisLine(x, plot.Top, x, plot.Bottom, gridBrush);
        }
    }

    private void DrawEmptyGrid(Rect plot)
    {
        AxisScale yScale = CreateAxisScale(
            0,
            100,
            Math.Max(2, (int)(plot.Height / 55)),
            includeZero: false,
            integerStep: false,
            expandBounds: false);
        AxisScale xScale = CreateAxisScale(
            0,
            100,
            Math.Max(2, (int)(plot.Width / 100)),
            includeZero: false,
            integerStep: false,
            expandBounds: false);

        DrawGrid(plot, yScale, xScale);
    }

    private void AddAxisLine(double x1, double y1, double x2, double y2, Brush brush)
    {
        ChartCanvas.Children.Add(new Line
        {
            X1 = x1,
            X2 = x2,
            Y1 = y1,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 1
        });
    }

    private void DrawTitle(double width)
    {
        TextBlock titleBlock = new()
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Foreground,
            MaxWidth = Math.Max(1, width - 24)
        };
        Canvas.SetLeft(titleBlock, 12);
        Canvas.SetTop(titleBlock, 6);
        ChartCanvas.Children.Add(titleBlock);

        titleBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double gearLeft = Math.Min(12 + titleBlock.DesiredSize.Width + 6, Math.Max(12, width - 26));
        Canvas.SetLeft(displaySettingsButton, gearLeft);
        Canvas.SetTop(displaySettingsButton, 5);
        displaySettingsButton.Visibility = Visibility.Visible;

        // Left overlay group (preset + frequency dropdowns) sits right after the
        // gear; the schematic group is centred. Both share the title line.
        double leftHostLeft = gearLeft + 22 + 8;
        Canvas.SetLeft(leftOverlayHost, leftHostLeft);
        Canvas.SetTop(leftOverlayHost, 2);
        leftOverlayHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double leftHostRight = leftHostLeft + leftOverlayHost.DesiredSize.Width;

        centerOverlayHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double centerWidth = centerOverlayHost.DesiredSize.Width;
        double centerLeft = Math.Max(leftHostRight + 12, (width - centerWidth) / 2);
        centerLeft = Math.Min(centerLeft, Math.Max(leftHostRight + 12, width - centerWidth - 120));
        Canvas.SetLeft(centerOverlayHost, centerLeft);
        Canvas.SetTop(centerOverlayHost, 2);
    }

    private double DrawLegend(double width)
    {
        double legendTop = Math.Max(28, LegendTopReserve);
        if (series.Count == 0)
        {
            curveVisibilityButton.Visibility = Visibility.Collapsed;
            return Math.Max(30, LegendTopReserve);
        }

        double top = legendTop;
        const double horizontalGap = 14;
        const double rowHeight = 21;
        List<LegendToken> tokens = [];
        bool hasLeftAxis = series.Any(item => item.YAxisGroup == 0);
        bool hasRightAxis = series.Any(item => item.YAxisGroup == 1);
        if (hasLeftAxis)
        {
            AddLegendAxisTokens(tokens, 0, "Левая ось", leftAxisUnits);
        }

        if (hasLeftAxis && hasRightAxis)
        {
            tokens.Add(CreateLegendToken("|", null, semiBold: false, opacity: 0.55));
        }

        if (hasRightAxis)
        {
            AddLegendAxisTokens(tokens, 1, "Правая ось", rightAxisUnits);
        }

        double availableWidth = Math.Max(1, width - 24);
        List<List<LegendToken>> rows = [[]];
        double currentWidth = 0;
        foreach (LegendToken token in tokens)
        {
            double gap = rows[^1].Count == 0 ? 0 : horizontalGap;
            if (rows[^1].Count > 0 && currentWidth + gap + token.Width > availableWidth)
            {
                rows.Add([]);
                currentWidth = 0;
                gap = 0;
            }

            rows[^1].Add(token);
            currentWidth += gap + token.Width;
        }

        double lastRowEndLeft = 12;
        double lastRowTop = legendTop;
        foreach (List<LegendToken> row in rows)
        {
            double rowWidth = row.Sum(token => token.Width) + horizontalGap * Math.Max(0, row.Count - 1);
            double left = Math.Max(12, (width - rowWidth) / 2);
            foreach (LegendToken token in row)
            {
                if (token.Series is { } item)
                {
                    ChartCanvas.Children.Add(new Line
                    {
                        X1 = left,
                        X2 = left + 15,
                        Y1 = top + 8,
                        Y2 = top + 8,
                        Stroke = new SolidColorBrush(ColorFromColorRef(item.ColorRef)),
                        StrokeThickness = 2
                    });
                    left += 20;
                }

                Canvas.SetLeft(token.Label, left);
                Canvas.SetTop(token.Label, top);
                ChartCanvas.Children.Add(token.Label);
                left += token.LabelWidth + horizontalGap;
            }

            lastRowEndLeft = left - horizontalGap;
            lastRowTop = top;
            top += rowHeight;
        }

        if (ShowCurveVisibilityToggle)
        {
            double eyeLeft = Math.Min(lastRowEndLeft + 6, Math.Max(12, width - 28));
            Canvas.SetLeft(curveVisibilityButton, eyeLeft);
            Canvas.SetTop(curveVisibilityButton, lastRowTop);
            curveVisibilityButton.Visibility = Visibility.Visible;
        }
        else
        {
            curveVisibilityButton.Visibility = Visibility.Collapsed;
        }

        return top;
    }

    private void AddLegendAxisTokens(List<LegendToken> tokens, int axisGroup, string axisName, string units)
    {
        List<ChartRenderSeries> axisSeries = series.Where(item => item.YAxisGroup == axisGroup).ToList();
        if (axisSeries.Count == 0)
        {
            return;
        }

        string axisHeader = string.IsNullOrWhiteSpace(units) ? $"[{axisName}]:" : $"[{axisName} ({units})]:";
        tokens.Add(CreateLegendToken(axisHeader, null, semiBold: true));
        bool isPreset = !string.IsNullOrWhiteSpace(presetDisplayName);
        bool useCondensedLabels = isPreset;
        if (isPreset)
        {
            tokens.Add(CreateLegendToken($"{presetDisplayName}:", null, semiBold: true));
        }
        else
        {
            string[] categories = axisSeries
                .Select(item => item.LegendCategory)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            useCondensedLabels = categories.Length == 1 && axisSeries.All(item => !string.IsNullOrWhiteSpace(item.LegendCategory));
            if (useCondensedLabels)
            {
                tokens.Add(CreateLegendToken(categories[0], null, semiBold: true));
            }
        }

        foreach (ChartRenderSeries item in axisSeries)
        {
            tokens.Add(CreateLegendToken(useCondensedLabels ? item.LegendLabel : item.DisplayName, item, semiBold: false));
        }
    }

    private LegendToken CreateLegendToken(string text, ChartRenderSeries? item, bool semiBold, double opacity = 1)
    {
        TextBlock label = new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Foreground,
            Opacity = opacity
        };
        double labelWidth = EstimateLegendTextWidth(text);
        return new LegendToken(label, item, labelWidth + (item is null ? 0 : 20), labelWidth);
    }

    private static double EstimateLegendTextWidth(string text)
    {
        double width = 2;
        foreach (char character in text)
        {
            width += character switch
            {
                ' ' => 3.2,
                ':' or '.' or ',' or '[' or ']' or '(' or ')' => 4.2,
                'I' or 'i' or 'l' or '1' => 4.5,
                _ when char.IsUpper(character) => 7.2,
                _ => 6.4
            };
        }

        return width;
    }

    private void DrawEmptyState(Rect plot)
    {
        TextBlock message = new()
        {
            Text = emptyStateMessage,
            FontSize = 14,
            Opacity = 0.72,
            Foreground = Foreground
        };

        message.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(message, plot.Left + Math.Max(0, (plot.Width - message.DesiredSize.Width) / 2));
        Canvas.SetTop(message, plot.Top + Math.Max(0, (plot.Height - message.DesiredSize.Height) / 2));
        ChartCanvas.Children.Add(message);
    }

    private (double Min, double Max)? GetAxisRange(int axisGroup)
    {
        List<float> finite = series
            .Where(item => item.YAxisGroup == axisGroup)
            .SelectMany(item => item.Values)
            .Where(float.IsFinite)
            .ToList();
        if (finite.Count == 0)
        {
            return null;
        }

        float min = finite.Min();
        float max = finite.Max();
        if (NearlyEqual(min, max))
        {
            min -= 1.0f;
            max += 1.0f;
        }

        return ApplyDisplayPadding((min, max));
    }

    private static (double Min, double Max) ApplyDisplayPadding((double Min, double Max) range)
    {
        double min = Math.Min(range.Min, range.Max);
        double max = Math.Max(range.Min, range.Max);
        double span = max - min;
        if (!(span > 0) || !double.IsFinite(span))
        {
            span = 1;
        }

        double padding = span * 0.05;
        return (min - padding, max + padding);
    }

    private void DrawLeftYAxisLabels(Rect plot, AxisScale scale, string units)
    {
        foreach (double value in EnumerateTicks(scale))
        {
            TextBlock label = CreateAxisLabel(FormatAxisValue(value, scale.Step, units));
            label.Width = Math.Max(1, plot.Left - 10);
            label.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, MapY(plot, value, scale.Min, scale.Max) - 9);
            ChartCanvas.Children.Add(label);
        }

        AddAxisUnitsLabel(units, Math.Max(2, plot.Left - 45), Math.Max(24, plot.Top - 23));
    }

    private void DrawRightYAxisLabels(Rect plot, AxisScale scale, string units)
    {
        foreach (double value in EnumerateTicks(scale))
        {
            AddAxisLabel(
                FormatAxisValue(value, scale.Step, units),
                plot.Right + 8,
                MapY(plot, value, scale.Min, scale.Max) - 9);
        }

        AddAxisUnitsLabel(units, plot.Right + 8, Math.Max(24, plot.Top - 23));
    }

    private (float Min, float Max) GetXRange(int pointCount)
    {
        List<float> finite = new(pointCount);
        for (int index = 0; index < pointCount; index++)
        {
            float value = GetXValue(index);
            if (float.IsFinite(value))
            {
                finite.Add(value);
            }
        }

        if (finite.Count == 0)
        {
            return (0.0f, Math.Max(1, pointCount - 1));
        }

        float min = finite.Min();
        float max = finite.Max();
        if (NearlyEqual(min, max))
        {
            min -= 1.0f;
            max += 1.0f;
        }

        return (min, max);
    }

    private void DrawXAxisLabels(Rect plot, AxisScale scale, bool integerLabels)
    {
        HashSet<string> renderedLabels = [];
        foreach (double value in EnumerateTicks(scale))
        {
            string text = integerLabels
                ? Math.Round(value).ToString("0", CultureInfo.CurrentCulture)
                : FormatAxisValue(value, scale.Step, string.Empty);
            if (!renderedLabels.Add(text))
            {
                continue;
            }

            TextBlock label = CreateAxisLabel(text);
            double labelWidth = EstimateLegendTextWidth(text);
            double x = MapX(plot, value, scale.Min, scale.Max) - labelWidth / 2;
            x = Math.Clamp(x, plot.Left - 3, plot.Right - labelWidth + 3);
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, plot.Bottom + 10);
            ChartCanvas.Children.Add(label);
        }

    }

    private void DrawSeries(Rect plot, ChartRenderSeries item, double minY, double maxY, double minX, double maxX)
    {
        double yRange = maxY - minY;
        double xRange = maxX - minX;
        Polyline? segment = null;
        for (int index = 0; index < item.Values.Count; index++)
        {
            float value = item.Values[index];
            float xValue = GetXValue(index);
            if (!float.IsFinite(value) || !float.IsFinite(xValue))
            {
                segment = null;
                continue;
            }

            if (segment is null)
            {
                segment = new Polyline
                {
                    Stroke = new SolidColorBrush(ColorFromColorRef(item.ColorRef)),
                    StrokeThickness = 1.6,
                    StrokeLineJoin = PenLineJoin.Round
                };
                ChartCanvas.Children.Add(segment);
            }

            double clampedX = Math.Clamp(xValue, minX, maxX);
            double clampedY = Math.Clamp(value, minY, maxY);
            double x = MapX(plot, clampedX, minX, maxX);
            double y = MapY(plot, clampedY, minY, maxY);
            segment.Points.Add(new Point(x, y));
        }
    }

    private float GetXValue(int index)
    {
        return index >= 0 && index < xValues.Count ? xValues[index] : index;
    }

    private void AddAxisLabel(string text, double left, double top)
    {
        TextBlock label = CreateAxisLabel(text);
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        ChartCanvas.Children.Add(label);
    }

    private TextBlock CreateAxisLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Opacity = 0.72,
            Foreground = Foreground
        };
    }

    private void AddAxisUnitsLabel(string units, double left, double top)
    {
        if (string.IsNullOrWhiteSpace(units))
        {
            return;
        }

        TextBlock label = CreateAxisLabel(units);
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        label.Opacity = 0.9;
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        ChartCanvas.Children.Add(label);
    }

    private static AxisScale CreateAxisScale(
        double min,
        double max,
        int targetSteps,
        bool includeZero,
        bool integerStep,
        bool expandBounds)
    {
        if (includeZero)
        {
            min = Math.Min(0, min);
            max = Math.Max(0, max);
        }

        if (Math.Abs(max - min) <= 0.000001)
        {
            double padding = Math.Max(1, Math.Abs(min) * 0.1);
            min -= padding;
            max += padding;
        }

        double step = CalculateNiceStep(max - min, targetSteps);
        if (integerStep)
        {
            step = Math.Max(1, step);
        }

        if (expandBounds)
        {
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
        }

        return new AxisScale(min, max, step);
    }

    private static double CalculateNiceStep(double range, int targetSteps)
    {
        double roughStep = range / Math.Max(1, targetSteps);
        if (!double.IsFinite(roughStep) || roughStep <= 0)
        {
            return 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        double normalized = roughStep / magnitude;
        double niceNormalized = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return niceNormalized * magnitude;
    }

    private static IEnumerable<double> EnumerateTicks(AxisScale scale)
    {
        double epsilon = scale.Step * 0.000001;
        double first = Math.Ceiling((scale.Min - epsilon) / scale.Step) * scale.Step;
        for (double value = first; value <= scale.Max + epsilon; value += scale.Step)
        {
            yield return Math.Abs(value) <= epsilon ? 0 : value;
        }
    }

    private static string FormatAxisValue(double value, double step, string units)
    {
        if (Math.Abs(value) <= Math.Abs(step) * 0.000001)
        {
            value = 0;
        }

        int precision;
        if (string.Equals(units, "mdeg", StringComparison.OrdinalIgnoreCase) || Math.Abs(step) >= 1)
        {
            precision = 0;
        }
        else
        {
            precision = Math.Clamp((int)Math.Ceiling(-Math.Log10(Math.Abs(step))), 0, 6);
        }

        return value.ToString($"F{precision}", CultureInfo.CurrentCulture);
    }

    private void ConfigureVerticalScrollBar()
    {
        leftYScrollContext = BuildAxisScrollContext(GetAxisRange(0), requestedLeftRange);
        rightYScrollContext = BuildAxisScrollContext(GetAxisRange(1), requestedRightRange);
        AxisScrollContext? primary = leftYScrollContext ?? rightYScrollContext;
        if (primary is not { } context)
        {
            SetScrollBarVisibility(VerticalAxisScrollBar, false);
            return;
        }

        VerticalAxisScrollBar.Minimum = 0;
        VerticalAxisScrollBar.Maximum = context.MaximumOffset;
        VerticalAxisScrollBar.ViewportSize = context.ViewRange;
        VerticalAxisScrollBar.SmallChange = Math.Max(context.MaximumOffset / 100.0, 0.000001);
        VerticalAxisScrollBar.LargeChange = Math.Max(context.ViewRange, VerticalAxisScrollBar.SmallChange);
        VerticalAxisScrollBar.Value = context.Value;
        SetScrollBarVisibility(VerticalAxisScrollBar, true);
    }

    private static void SetScrollBarVisibility(ScrollBar scrollBar, bool isVisible)
    {
        if (!isVisible)
        {
            scrollBar.Visibility = Visibility.Collapsed;
            return;
        }

        // A standalone WinUI ScrollBar can remain in the NoIndicator visual state even
        // when Visibility is Visible. Keep the desktop indicator active explicitly so
        // zoom and manually selected ranges always expose their navigation controls.
        scrollBar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
        scrollBar.Visibility = Visibility.Visible;
        scrollBar.ApplyTemplate();
        VisualStateManager.GoToState(scrollBar, "MouseIndicator", useTransitions: false);
    }

    private static AxisScrollContext? BuildAxisScrollContext(
        (double Min, double Max)? dataRange,
        (double Min, double Max)? selectedRange)
    {
        if (dataRange is not { } data || selectedRange is not { } selected)
        {
            return null;
        }

        double selectedMin = Math.Min(selected.Min, selected.Max);
        double selectedMax = Math.Max(selected.Min, selected.Max);
        double viewRange = selectedMax - selectedMin;
        if (!(viewRange > 0) || !double.IsFinite(viewRange))
        {
            return null;
        }

        double fullMin = Math.Min(data.Min, data.Max);
        double fullMax = Math.Max(data.Min, data.Max);
        double totalRange = fullMax - fullMin;
        if (!(totalRange > viewRange))
        {
            double dataCenter = (fullMin + fullMax) / 2.0;
            double expandedHalf = viewRange * 0.75;
            fullMin = dataCenter - expandedHalf;
            fullMax = dataCenter + expandedHalf;
            totalRange = fullMax - fullMin;
        }

        double maximumOffset = Math.Max(0, totalRange - viewRange);
        double value = Math.Clamp(fullMax - selectedMax, 0, maximumOffset);
        return new AxisScrollContext(fullMin, fullMax, viewRange, maximumOffset, value);
    }

    private void HorizontalAxisScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        if (updatingScrollBars)
        {
            return;
        }

        HorizontalScrollRequested?.Invoke(
            this,
            new ChartXScrollRequestedEventArgs((int)Math.Round(e.NewValue)));
    }

    private void VerticalAxisScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        if (updatingScrollBars)
        {
            return;
        }

        AxisScrollContext? primary = leftYScrollContext ?? rightYScrollContext;
        if (primary is not { } context)
        {
            return;
        }

        double ratio = context.MaximumOffset > 0
            ? Math.Clamp(e.NewValue / context.MaximumOffset, 0, 1)
            : 0;
        VerticalScrollRequested?.Invoke(
            this,
            new ChartYScrollRequestedEventArgs(
                leftYScrollContext?.GetRange(ratio),
                rightYScrollContext?.GetRange(ratio)));
    }

    private void UpdateScrollBarLayout(Rect plot, double width, double height)
    {
        if (width <= 0 || height <= 0 || plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        double horizontalRight = Math.Max(4, width - plot.Right);
        HorizontalAxisScrollBar.Margin = new Thickness(
            plot.Left,
            0,
            horizontalRight,
            HorizontalScrollBarBottomMargin);
        VerticalAxisScrollBar.Margin = new Thickness(0, plot.Top, 4, Math.Max(0, height - plot.Bottom));
    }

    private void HideScrollBars()
    {
        updatingScrollBars = true;
        SetScrollBarVisibility(HorizontalAxisScrollBar, false);
        SetScrollBarVisibility(VerticalAxisScrollBar, false);
        leftYScrollContext = null;
        rightYScrollContext = null;
        updatingScrollBars = false;
    }

    private void ChartCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(ChartCanvas);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point point = currentPoint.Position;
        if (!currentPlot.Contains(point))
        {
            return;
        }

        foreach (RenderedNoteMarker marker in renderedNoteMarkers)
        {
            double dx = point.X - marker.ScreenX;
            double dy = point.Y - marker.ScreenY;
            if ((dx * dx) + (dy * dy) <= 36)
            {
                NoteMarkerActivated?.Invoke(this, new ChartNoteMarkerEventArgs(marker.NoteId));
                e.Handled = true;
                return;
            }
        }

        dragStart = point;
        ChartCanvas.CapturePointer(e.Pointer);
        selectionRectangle = new Rectangle
        {
            Fill = new SolidColorBrush(Colors.DodgerBlue) { Opacity = 0.14 },
            Stroke = new SolidColorBrush(Colors.DodgerBlue) { Opacity = 0.85 },
            StrokeThickness = 1
        };
        ChartCanvas.Children.Add(selectionRectangle);
        e.Handled = true;
    }

    private void ChartCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dragStart is not { } start || selectionRectangle is null)
        {
            return;
        }

        Point current = ClampToPlot(e.GetCurrentPoint(ChartCanvas).Position);
        UpdateSelectionRectangle(start, current);
        e.Handled = true;
    }

    private void ChartCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (dragStart is not { } start)
        {
            return;
        }

        Point end = ClampToPlot(e.GetCurrentPoint(ChartCanvas).Position);
        ChartCanvas.ReleasePointerCapture(e.Pointer);
        ClearSelection();

        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        bool isZoomGesture = IsFlowMode ? height > 10 : width > 10 && height > 10;
        if (!isZoomGesture)
        {
            SelectPoint(start);
            e.Handled = true;
            return;
        }

        crosshair = null;
        double startFraction = ResolveSelectionFraction(start.X);
        double endFraction = ResolveSelectionFraction(end.X);
        ZoomRequested?.Invoke(this, new ChartZoomRequestedEventArgs(
            startFraction,
            endFraction,
            ToSelectedYRange(start.Y, end.Y, currentLeftScale),
            ToSelectedYRange(start.Y, end.Y, currentRightScale)));
        e.Handled = true;
    }

    private void ChartCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Point position = e.GetPosition(ChartCanvas);
        ChartContextRequested?.Invoke(this, new ChartContextRequestedEventArgs(position, HasCrosshair));
        e.Handled = true;
    }

    private void SelectPoint(Point point)
    {
        if (xValues.Count == 0 || currentXScale is not { } xScale || currentLeftScale is not { } leftScale)
        {
            crosshair = null;
            Render();
            return;
        }

        double displayFraction = Math.Clamp((point.X - currentPlot.Left) / currentPlot.Width, 0, 1);
        double selectedX = xScale.Min + displayFraction * (xScale.Max - xScale.Min);
        int closestIndex = -1;
        double closestDistance = double.PositiveInfinity;
        for (int index = 0; index < xValues.Count; index++)
        {
            float xValue = xValues[index];
            if (!float.IsFinite(xValue))
            {
                continue;
            }

            double distance = Math.Abs(xValue - selectedX);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        if (closestIndex < 0)
        {
            crosshair = null;
            Render();
            return;
        }

        Point clamped = ClampToPlot(point);
        double yLeft = ResolveYValue(clamped.Y, leftScale);
        bool hasRightAxis = currentRightScale is not null;
        double yRight = currentRightScale is { } rightScale
            ? ResolveYValue(clamped.Y, rightScale)
            : 0;
        crosshair = new CrosshairState(clamped);
        Render();
        PointSelected?.Invoke(
            this,
            new ChartPointSelectedEventArgs(
                visibleStartIndex + closestIndex,
                yLeft,
                yRight,
                hasRightAxis));
    }

    private double ResolveYValue(double screenY, AxisScale scale)
    {
        double ratio = (currentPlot.Bottom - screenY) / currentPlot.Height;
        return scale.Min + ratio * (scale.Max - scale.Min);
    }

    private double ResolveSelectionFraction(double screenX)
    {
        double displayFraction = Math.Clamp((screenX - currentPlot.Left) / currentPlot.Width, 0, 1);
        if (xValues.Count <= 1 || currentXScale is not { } scale)
        {
            return displayFraction;
        }

        double selectedX = scale.Min + displayFraction * (scale.Max - scale.Min);
        int closestIndex = 0;
        double closestDistance = double.PositiveInfinity;
        for (int index = 0; index < xValues.Count; index++)
        {
            float value = xValues[index];
            if (!float.IsFinite(value))
            {
                continue;
            }

            double distance = Math.Abs(value - selectedX);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        if (double.IsPositiveInfinity(closestDistance))
        {
            return displayFraction;
        }

        return closestIndex / (double)(xValues.Count - 1);
    }

    private void ChartCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ClearSelection();
    }

    private Point ClampToPlot(Point point)
    {
        return new Point(
            Math.Clamp(point.X, currentPlot.Left, currentPlot.Right),
            Math.Clamp(point.Y, currentPlot.Top, currentPlot.Bottom));
    }

    private void UpdateSelectionRectangle(Point start, Point end)
    {
        if (selectionRectangle is null)
        {
            return;
        }

        double left = IsFlowMode ? currentPlot.Left : Math.Min(start.X, end.X);
        double width = IsFlowMode ? currentPlot.Width : Math.Abs(end.X - start.X);
        Canvas.SetLeft(selectionRectangle, left);
        Canvas.SetTop(selectionRectangle, Math.Min(start.Y, end.Y));
        selectionRectangle.Width = width;
        selectionRectangle.Height = Math.Abs(end.Y - start.Y);
    }

    private (double Min, double Max)? ToSelectedYRange(double firstY, double secondY, AxisScale? scale)
    {
        if (scale is not { } axis)
        {
            return null;
        }

        double first = axis.Max - ((firstY - currentPlot.Top) / currentPlot.Height) * (axis.Max - axis.Min);
        double second = axis.Max - ((secondY - currentPlot.Top) / currentPlot.Height) * (axis.Max - axis.Min);
        return (Math.Min(first, second), Math.Max(first, second));
    }

    private void ClearSelection()
    {
        if (selectionRectangle is not null)
        {
            ChartCanvas.Children.Remove(selectionRectangle);
        }

        selectionRectangle = null;
        dragStart = null;
    }

    private static double MapX(Rect plot, double value, double min, double max)
    {
        return plot.Left + ((value - min) / Math.Max(double.Epsilon, max - min)) * plot.Width;
    }

    private static double MapY(Rect plot, double value, double min, double max)
    {
        return plot.Bottom - ((value - min) / Math.Max(double.Epsilon, max - min)) * plot.Height;
    }

    private static Color ColorFromColorRef(int colorRef)
    {
        return Color.FromArgb(
            255,
            (byte)(colorRef & 0xFF),
            (byte)((colorRef >> 8) & 0xFF),
            (byte)((colorRef >> 16) & 0xFF));
    }

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private static Color GetAccentColor()
    {
        object? accent = Application.Current.Resources["SystemAccentColor"];
        return accent is Color color ? color : Colors.DodgerBlue;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return Math.Abs(left - right) <= 0.000001f;
    }

    private readonly record struct AxisScale(double Min, double Max, double Step);

    private readonly record struct AxisScrollContext(
        double FullMin,
        double FullMax,
        double ViewRange,
        double MaximumOffset,
        double Value)
    {
        public (double Min, double Max) GetRange(double ratio)
        {
            double offset = Math.Clamp(ratio, 0, 1) * MaximumOffset;
            double max = FullMax - offset;
            double min = Math.Max(FullMin, max - ViewRange);
            return (min, min + ViewRange);
        }
    }

    private readonly record struct RenderedNoteMarker(string NoteId, double ScreenX, double ScreenY);

    private readonly record struct NoteLabelCandidate(double AnchorX, double AnchorY, string Text);

    private readonly record struct CrosshairState(Point Position);

    private sealed record LegendToken(TextBlock Label, ChartRenderSeries? Series, double Width, double LabelWidth);
}
