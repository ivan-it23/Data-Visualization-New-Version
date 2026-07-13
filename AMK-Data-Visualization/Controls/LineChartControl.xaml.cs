using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AMK.DataVisualization.App.Controls;

public sealed partial class LineChartControl : UserControl
{
    private readonly List<float> values = [];
    private string title = string.Empty;
    private string units = string.Empty;
    private string emptyStateMessage = "Нет данных графика";

    public LineChartControl()
    {
        InitializeComponent();
    }

    public void SetSeries(IReadOnlyList<float> sourceValues, string seriesTitle, string seriesUnits)
    {
        values.Clear();
        values.AddRange(sourceValues);
        title = seriesTitle;
        units = seriesUnits;
        Render();
    }

    public void ClearSeries(string emptyTitle = "Файл не загружен")
    {
        values.Clear();
        title = emptyTitle;
        units = string.Empty;
        emptyStateMessage = "Нет данных графика";
        Render();
    }

    public void ShowLegacyEmptyState(string chartTitle, string message)
    {
        values.Clear();
        title = chartTitle;
        units = string.Empty;
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

        Thickness plotPadding = new(56, 34, 24, 42);
        Rect plot = new(
            plotPadding.Left,
            plotPadding.Top,
            Math.Max(1, width - plotPadding.Left - plotPadding.Right),
            Math.Max(1, height - plotPadding.Top - plotPadding.Bottom));

        DrawPlotFrame(plot);
        DrawTitle(width);

        List<float> finiteValues = values.Where(float.IsFinite).ToList();
        if (finiteValues.Count == 0)
        {
            DrawEmptyState(plot);
            return;
        }

        float minY = finiteValues.Min();
        float maxY = finiteValues.Max();
        if (NearlyEqual(minY, maxY))
        {
            minY -= 1.0f;
            maxY += 1.0f;
        }

        DrawYAxisLabels(plot, minY, maxY);
        DrawXAxisLabels(plot);
        DrawSeries(plot, minY, maxY);
    }

    private void DrawPlotFrame(Rect plot)
    {
        SolidColorBrush gridBrush = new(Colors.Gray) { Opacity = 0.24 };
        SolidColorBrush axisBrush = new(Colors.Gray) { Opacity = 0.62 };

        const int gridLineCount = 5;
        for (int i = 0; i <= gridLineCount; i++)
        {
            double y = plot.Top + plot.Height * i / gridLineCount;
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

        ChartCanvas.Children.Add(new Line
        {
            X1 = plot.Left,
            X2 = plot.Left,
            Y1 = plot.Top,
            Y2 = plot.Bottom,
            Stroke = axisBrush,
            StrokeThickness = 1
        });

        ChartCanvas.Children.Add(new Line
        {
            X1 = plot.Left,
            X2 = plot.Right,
            Y1 = plot.Bottom,
            Y2 = plot.Bottom,
            Stroke = axisBrush,
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
            Foreground = Foreground
        };
        Canvas.SetLeft(titleBlock, 12);
        Canvas.SetTop(titleBlock, 8);
        titleBlock.MaxWidth = Math.Max(1, width - 24);
        ChartCanvas.Children.Add(titleBlock);
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

    private void DrawYAxisLabels(Rect plot, float minY, float maxY)
    {
        string maxText = FormatValue(maxY);
        string minText = FormatValue(minY);
        if (!string.IsNullOrWhiteSpace(units))
        {
            maxText = $"{maxText} {units}";
        }

        AddAxisLabel(maxText, 8, plot.Top - 9);
        AddAxisLabel(minText, 8, plot.Bottom - 11);
    }

    private void DrawXAxisLabels(Rect plot)
    {
        AddAxisLabel("0", plot.Left - 3, plot.Bottom + 12);
        if (values.Count > 0)
        {
            string endText = (values.Count - 1).ToString();
            TextBlock endLabel = CreateAxisLabel(endText);
            endLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(endLabel, plot.Right - endLabel.DesiredSize.Width + 3);
            Canvas.SetTop(endLabel, plot.Bottom + 12);
            ChartCanvas.Children.Add(endLabel);
        }
    }

    private void DrawSeries(Rect plot, float minY, float maxY)
    {
        Polyline line = new()
        {
            Stroke = new SolidColorBrush(GetAccentColor()),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        double yRange = maxY - minY;
        int denominator = Math.Max(1, values.Count - 1);
        for (int i = 0; i < values.Count; i++)
        {
            float value = values[i];
            if (!float.IsFinite(value))
            {
                continue;
            }

            double x = plot.Left + plot.Width * i / denominator;
            double y = plot.Bottom - ((value - minY) / yRange) * plot.Height;
            line.Points.Add(new Point(x, y));
        }

        ChartCanvas.Children.Add(line);
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

    private static string FormatValue(float value)
    {
        return Math.Abs(value) >= 1000.0f || Math.Abs(value) < 0.01f
            ? value.ToString("0.###E+0")
            : value.ToString("0.###");
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
