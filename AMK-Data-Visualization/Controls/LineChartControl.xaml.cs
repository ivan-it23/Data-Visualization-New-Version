using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using Windows.Foundation;
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

public sealed partial class LineChartControl : UserControl
{
    private readonly List<ChartRenderSeries> series = [];
    private string title = string.Empty;
    private string leftAxisUnits = string.Empty;
    private string rightAxisUnits = string.Empty;
    private IReadOnlyList<float> xValues = [];
    private string xAxisTitle = "Номер кадра";
    private string presetDisplayName = string.Empty;
    private string emptyStateMessage = "Нет данных графика";

    public LineChartControl()
    {
        InitializeComponent();
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
        string activePresetDisplayName)
    {
        series.Clear();
        series.AddRange(sourceSeries);
        title = chartTitle;
        leftAxisUnits = leftUnits;
        rightAxisUnits = rightUnits;
        xValues = sourceXValues;
        xAxisTitle = horizontalAxisTitle;
        presetDisplayName = activePresetDisplayName;
        emptyStateMessage = "Нет данных графика";
        Render();
    }

    public void ClearSeries(string emptyTitle = "Файл не загружен")
    {
        series.Clear();
        title = emptyTitle;
        leftAxisUnits = string.Empty;
        rightAxisUnits = string.Empty;
        xValues = [];
        xAxisTitle = "Номер кадра";
        presetDisplayName = string.Empty;
        emptyStateMessage = "Нет данных графика";
        Render();
    }

    public void ShowLegacyEmptyState(string chartTitle, string message)
    {
        series.Clear();
        title = chartTitle;
        leftAxisUnits = string.Empty;
        rightAxisUnits = string.Empty;
        xValues = [];
        xAxisTitle = "Номер кадра";
        presetDisplayName = string.Empty;
        emptyStateMessage = message;
        Render();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Render();
    }

    private void Render()
    {
        ChartCanvas.Children.Clear();

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

        List<float> allFinite = series.SelectMany(item => item.Values).Where(float.IsFinite).ToList();
        if (allFinite.Count == 0)
        {
            DrawPlotFrame(plot, hasRightAxis);
            DrawEmptyState(plot);
            return;
        }

        (float LeftMin, float LeftMax)? leftRange = GetAxisRange(0);
        (float LeftMin, float LeftMax)? rightRange = GetAxisRange(1);
        int pointCount = series.Count == 0 ? 0 : series.Max(item => item.Values.Count);
        (float Min, float Max) xRange = GetXRange(pointCount);
        bool isFrameAxis = string.Equals(xAxisTitle, "Номер кадра", StringComparison.Ordinal);
        AxisScale? leftScale = leftRange is { } left
            ? CreateAxisScale(left.LeftMin, left.LeftMax, Math.Max(2, (int)(plot.Height / 55)), includeZero: true, integerStep: false, expandBounds: true)
            : null;
        AxisScale? rightScale = rightRange is { } right
            ? CreateAxisScale(right.LeftMin, right.LeftMax, Math.Max(2, (int)(plot.Height / 55)), includeZero: true, integerStep: false, expandBounds: true)
            : null;
        AxisScale xScale = CreateAxisScale(
            xRange.Min,
            xRange.Max,
            Math.Max(2, (int)(plot.Width / 100)),
            includeZero: isFrameAxis,
            integerStep: isFrameAxis,
            expandBounds: false);

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
    }

    private double DrawLegend(double width)
    {
        if (series.Count == 0)
        {
            return 30;
        }

        double top = 28;
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

            top += rowHeight;
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

    private (float LeftMin, float LeftMax)? GetAxisRange(int axisGroup)
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

        return (min, max);
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

        TextBlock titleLabel = CreateAxisLabel(xAxisTitle);
        double titleWidth = EstimateLegendTextWidth(xAxisTitle);
        Canvas.SetLeft(titleLabel, plot.Left + Math.Max(0, (plot.Width - titleWidth) / 2));
        Canvas.SetTop(titleLabel, plot.Bottom + 25);
        ChartCanvas.Children.Add(titleLabel);
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

            double x = MapX(plot, xValue, minX, maxX);
            double y = MapY(plot, value, minY, maxY);
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

    private sealed record LegendToken(TextBlock Label, ChartRenderSeries? Series, double Width, double LabelWidth);
}
