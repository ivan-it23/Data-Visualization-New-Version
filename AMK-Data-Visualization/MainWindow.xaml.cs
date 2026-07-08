using System.Runtime.InteropServices;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.IO.DataFiles;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultTransmitterIndex = 0;
    private const int ShowMaximized = 3;
    private readonly ToolDataRepository repository = new();
    private readonly ChartConfiguration graphConfiguration = CreateDefaultGraphConfiguration();

    public MainWindow()
    {
        InitializeComponent();
        GraphControl.ClearSeries("Файл не загружен");
        MaximizeLikeLegacyApplication();
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".biv");
        picker.FileTypeFilter.Add(".DEV");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            repository.LoadDataFromFile(file.Path);
            RenderDefaultGraph(file.Path);
            SetStatus("Готов");
            UpdateStatusBarFileInfo(file.Path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            repository.ClearData();
            GraphControl.ClearSeries("Ошибка загрузки");
            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo(file.Path);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RenderDefaultGraph(string filePath)
    {
        if (!repository.HasData || !repository.TryGetRecord(0, out var firstRecord))
        {
            GraphControl.ClearSeries("Файл не загружен");
            SetStatus("Готов");
            UpdateStatusBarFileInfo(filePath);
            return;
        }

        _ = UniversalCanonicalAdapter.BuildIdentity(firstRecord.Signature);
        _ = repository.TryGetRecordMetadata(0, out ToolRecordMetadata _);

        CurveDefinition curve = graphConfiguration.Curves[0];
        ConfiguredChartData chartData = ChartDataCalculator.CalculateChartDataFromConfiguration(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration);

        IReadOnlyList<float> series = chartData.LeftAxisData.Count > 0
            ? chartData.LeftAxisData[0]
            : [];

        GraphControl.SetSeries(series, curve.DisplayName, curve.Units);
        UpdateStatusBarFileInfo(filePath);
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private void UpdateStatusBarFileInfo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            FolderStatusIcon.Visibility = Visibility.Collapsed;
            LoadedFileStatusTextBlock.Text = "Файл не загружен";
            return;
        }

        FolderStatusIcon.Visibility = repository.HasData ? Visibility.Visible : Visibility.Collapsed;
        LoadedFileStatusTextBlock.Text = Path.GetFileName(filePath);
    }

    private void MaximizeLikeLegacyApplication()
    {
        ShowWindow(WindowNative.GetWindowHandle(this), ShowMaximized);
    }

    private static ChartConfiguration CreateDefaultGraphConfiguration()
    {
        ChartConfiguration configuration = new();
        configuration.AddCurve(new CurveDefinition(
            "T1_R1_AMP_400",
            "T1 Амплитуда R1",
            "mV",
            ElementType.Transmitter,
            DefaultTransmitterIndex,
            DataType.AmplitudeRx1,
            FrequencyBand.Freq400KHz));
        return configuration;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
