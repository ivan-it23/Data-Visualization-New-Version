using System.Runtime.InteropServices;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.IO.DataFiles;
using AMK.DataVisualization.App.Layout;
using AMK.DataVisualization.App.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultTransmitterIndex = 0;
    private const int ShowMaximized = 3;
    private readonly ChartWorkspace chartWorkspace = new(
        CreateDefaultGraphConfiguration(),
        new ChartConfiguration());

    public MainWindow()
    {
        InitializeComponent();
        ShowLegacyEmptyState(1, hasData: false);
        ShowLegacyEmptyState(2, hasData: false);
        UpdateDualModeLayout();
        MaximizeLikeLegacyApplication();
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = CreateDataFilePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            chartWorkspace.Graph1Repository.LoadDataFromFile(file.Path);
            RenderGraph(1);
            RenderGraph(2);
            SetStatus("Готов");
            UpdateStatusBarFileInfo();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            chartWorkspace.Graph1Repository.ClearData();
            GraphControl.ClearSeries("Ошибка загрузки");
            if (!chartWorkspace.IsGraph2Independent)
            {
                Graph2Control.ClearSeries("Ошибка загрузки");
            }

            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo();
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenGraph2FileButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = CreateDataFilePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            // The legacy handler enters independent mode before loading the selected file.
            chartWorkspace.SetGraph2Mode(Graph2DataMode.IndependentFile);
            UpdateGraph2DataModeUi();
            chartWorkspace.Graph2IndependentRepository.LoadDataFromFile(file.Path);
            RenderGraph(2);
            SetStatus("Готов");
            UpdateStatusBarFileInfo();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            Graph2Control.ClearSeries("Ошибка загрузки");
            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo();
        }
    }

    private void Graph2LinkedModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        chartWorkspace.SetGraph2Mode(Graph2DataMode.LinkedToGraph1);
        UpdateGraph2DataModeUi();
        RenderGraph(2);
        UpdateStatusBarFileInfo();
    }

    private void Graph2IndependentModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        chartWorkspace.SetGraph2Mode(Graph2DataMode.IndependentFile);
        UpdateGraph2DataModeUi();
        RenderGraph(2);
        UpdateStatusBarFileInfo();
    }

    private void DualModeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateDualModeLayout();
        RenderGraph(2);
    }

    private void DualModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateDualModeLayout();
    }

    private void RenderGraph(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        var graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        ChartConfiguration graphConfiguration = chartWorkspace.GetConfigurationForGraph(graphNumber);

        if (!repository.HasData || !repository.TryGetRecord(0, out var firstRecord))
        {
            ShowLegacyEmptyState(graphNumber, hasData: false);
            return;
        }

        _ = UniversalCanonicalAdapter.BuildIdentity(firstRecord.Signature);
        _ = repository.TryGetRecordMetadata(0, out ToolRecordMetadata _);

        if (graphConfiguration.Curves.Count == 0)
        {
            ShowLegacyEmptyState(graphNumber, hasData: true);
            return;
        }

        CurveDefinition curve = graphConfiguration.Curves[0];
        ConfiguredChartData chartData = ChartDataCalculator.CalculateChartDataFromConfiguration(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration);

        IReadOnlyList<float> series = chartData.LeftAxisData.Count > 0
            ? chartData.LeftAxisData[0]
            : [];

        graphControl.SetSeries(series, curve.DisplayName, curve.Units);
    }

    private void ShowLegacyEmptyState(int graphNumber, bool hasData)
    {
        var graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        graphControl.ShowLegacyEmptyState(
            $"График {graphNumber}",
            hasData
                ? "Нажмите 'Схема прибора' для настройки графика"
                : "Сначала откройте файл данных");
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private void UpdateDualModeLayout()
    {
        bool dualMode = DualModeCheckBox.IsChecked == true;
        Chart2TopGapRow.Height = new GridLength(dualMode ? LegacyMainWindowLayout.ElementSpacing : 0.0);
        Chart2PanelRow.Height = new GridLength(dualMode ? LegacyMainWindowLayout.ControlPanelHeight : 0.0);
        Chart2BottomGapRow.Height = new GridLength(dualMode ? LegacyMainWindowLayout.ElementSpacing : 0.0);
        Graph2Row.Height = new GridLength(dualMode ? 1.0 : 0.0, dualMode ? GridUnitType.Star : GridUnitType.Pixel);

        Chart2ControlPanel.Visibility = dualMode ? Visibility.Visible : Visibility.Collapsed;
        Graph2Control.Visibility = dualMode ? Visibility.Visible : Visibility.Collapsed;
        Graph2OverlayControls.Visibility = dualMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateGraph2DataModeUi();
    }

    private void UpdateGraph2DataModeUi()
    {
        Graph2LinkedModeMenuItem.IsChecked = !chartWorkspace.IsGraph2Independent;
        Graph2IndependentModeMenuItem.IsChecked = chartWorkspace.IsGraph2Independent;
        Graph2OpenFileButton.Visibility = chartWorkspace.IsGraph2Independent && DualModeCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateStatusBarFileInfo()
    {
        bool hasAnyData = chartWorkspace.Graph1Repository.HasData || chartWorkspace.Graph2IndependentRepository.HasData;
        FolderStatusIcon.Visibility = hasAnyData ? Visibility.Visible : Visibility.Collapsed;
        LoadedFileStatusTextBlock.Text = chartWorkspace.GetStatusFileInformation();
    }

    private static FileOpenPicker CreateDataFilePicker()
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".biv");
        picker.FileTypeFilter.Add(".DEV");
        return picker;
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
