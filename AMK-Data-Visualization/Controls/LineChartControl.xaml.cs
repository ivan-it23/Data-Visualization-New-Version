using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AMK.DataVisualization.App.Controls;

public sealed record ChartRenderSeries(
    IReadOnlyList<float> Values,
    string DisplayName,
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
            [new ChartRenderSeries(sourceValues, seriesTitle, seriesUnits, ToColorRef(GetAccentColor()), 0)],
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
        Thickness plotPadding = new(66, Math.Max(40, legendBottom + 8), hasRightAxis ? 72 : 24, 42);
        Rect plot = new(
            plotPadding.Left,
            plotPadding.Top,
            Math.Max(1, width - plotPadding.Left - plotPadding.Right),
            Math.Max(1, height - plotPadding.Top - plotPadding.Bottom));

        DrawPlotFrame(plot, hasRightAxis);

        List<float> allFinite = series.SelectMany(item => item.Values).Where(float.IsFinite).ToList();
        if (allFinite.Count == 0)
        {
            DrawEmptyState(plot);
            return;
        }

        (float LeftMin, float LeftMax)? leftRange = GetAxisRange(0);
        (float LeftMin, float LeftMax)? rightRange = GetAxisRange(1);
        if (leftRange is { } left)
        {
            DrawLeftYAxisLabels(plot, left.LeftMin, left.LeftMax, leftAxisUnits);
        }

        if (rightRange is { } right)
        {
            DrawRightYAxisLabels(plot, right.LeftMin, right.LeftMax, rightAxisUnits);
        }

        int pointCount = series.Count == 0 ? 0 : series.Max(item => item.Values.Count);
        (float Min, float Max) xRange = GetXRange(pointCount);
        DrawXAxisLabels(plot, xRange.Min, xRange.Max);

        foreach (ChartRenderSeries item in series)
        {
            (float LeftMin, float LeftMax)? range = item.YAxisGroup == 1 ? rightRange : leftRange;
            if (range is { } valueRange)
            {
                DrawSeries(plot, item, valueRange.LeftMin, valueRange.LeftMax, xRange.Min, xRange.Max);
            }
        }
    }

    private void DrawPlotFrame(Rect plot, bool hasRightAxis)
    {
        SolidColorBrush gridBrush = new(Colors.Gray) { Opacity = 0.24 };
        SolidColorBrush axisBrush = new(Colors.Gray) { Opacity = 0.62 };

        const int gridLineCount = 5;
        for (int index = 0; index <= gridLineCount; index++)
        {
            double y = plot.Top + plot.Height * index / gridLineCount;
            ChartCanvas.Children.Add(new Line
            {
                X1 = plot.Left,
                X2 = plot.Right,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1
            });
        }

        AddAxisLine(plot.Left, plot.Top, plot.Left, plot.Bottom, axisBrush);
        AddAxisLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom, axisBrush);
        if (hasRightAxis)
        {
            AddAxisLine(plot.Right, plot.Top, plot.Right, plot.Bottom, axisBrush);
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

        double left = 12;
        double top = 28;
        const double horizontalGap = 14;
        const double rowHeight = 22;
        if (!string.IsNullOrWhiteSpace(presetDisplayName))
        {
            TextBlock presetLabel = new()
            {
                Text = $"{presetDisplayName}:",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Foreground
            };
            presetLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(presetLabel, left);
            Canvas.SetTop(presetLabel, top);
            ChartCanvas.Children.Add(presetLabel);
            left += presetLabel.DesiredSize.Width + 10;
        }

        foreach (ChartRenderSeries item in series)
        {
            StackPanel legendItem = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5
            };
            legendItem.Children.Add(new Rectangle
            {
                Width = 18,
                Height = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(ColorFromColorRef(item.ColorRef))
            });
            legendItem.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 11,
                Foreground = Foreground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 170
            });
            legendItem.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            if (left > 12 && left + legendItem.DesiredSize.Width > width - 12)
            {
                left = 12;
                top += rowHeight;
            }

            Canvas.SetLeft(legendItem, left);
            Canvas.SetTop(legendItem, top);
            ChartCanvas.Children.Add(legendItem);
            left += legendItem.DesiredSize.Width + horizontalGap;
        }

        return top + rowHeight;
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

    private void DrawLeftYAxisLabels(Rect plot, float min, float max, string units)
    {
        string maxText = AppendUnits(FormatValue(max), units);
        string minText = AppendUnits(FormatValue(min), units);
        AddAxisLabel(maxText, 8, plot.Top - 9);
        AddAxisLabel(minText, 8, plot.Bottom - 11);
    }

    private void DrawRightYAxisLabels(Rect plot, float min, float max, string units)
    {
        AddAxisLabel(AppendUnits(FormatValue(max), units), plot.Right + 8, plot.Top - 9);
        AddAxisLabel(AppendUnits(FormatValue(min), units), plot.Right + 8, plot.Bottom - 11);
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

    private void DrawXAxisLabels(Rect plot, float min, float max)
    {
        AddAxisLabel(FormatValue(min), plot.Left - 3, plot.Bottom + 12);

        TextBlock endLabel = CreateAxisLabel(FormatValue(max));
        endLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(endLabel, plot.Right - endLabel.DesiredSize.Width + 3);
        Canvas.SetTop(endLabel, plot.Bottom + 12);
        ChartCanvas.Children.Add(endLabel);

        TextBlock titleLabel = CreateAxisLabel(xAxisTitle);
        titleLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(titleLabel, plot.Left + Math.Max(0, (plot.Width - titleLabel.DesiredSize.Width) / 2));
        Canvas.SetTop(titleLabel, plot.Bottom + 25);
        ChartCanvas.Children.Add(titleLabel);
    }

    private void DrawSeries(Rect plot, ChartRenderSeries item, float minY, float maxY, float minX, float maxX)
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

            double x = plot.Left + ((xValue - minX) / xRange) * plot.Width;
            double y = plot.Bottom - ((value - minY) / yRange) * plot.Height;
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

    private static string AppendUnits(string value, string units)
    {
        return string.IsNullOrWhiteSpace(units) ? value : $"{value} {units}";
    }

    private static string FormatValue(float value)
    {
        return Math.Abs(value) >= 1000.0f || Math.Abs(value) < 0.01f
            ? value.ToString("0.###E+0")
            : value.ToString("0.###");
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
}
