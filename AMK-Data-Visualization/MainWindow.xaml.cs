using System.Runtime.InteropServices;
using AMK.DataVisualization.App.Controls;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.IO.DataFiles;
using AMK.DataVisualization.App.Layout;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.App.Views;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private const int ShowMaximized = 3;
    private readonly CurveSelectionService curveSelectionService = new();
    private readonly ChartWorkspace chartWorkspace = new(new ChartConfiguration(), new ChartConfiguration());
    private SchematicWindow? schematicWindow;

    public MainWindow()
    {
        InitializeComponent();
        ShowLegacyEmptyState(1, hasData: false);
        ShowLegacyEmptyState(2, hasData: false);
        UpdateDualModeLayout();
        MaximizeLikeLegacyApplication();
        Closed += MainWindow_Closed;
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
            schematicWindow?.RefreshDataContext(1, 2);
            RenderGraph(1);
            RenderGraph(2);
            SetStatus("Готов");
            UpdateStatusBarFileInfo();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            chartWorkspace.Graph1Repository.ClearData();
            schematicWindow?.RefreshDataContext(1, 2);
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
            schematicWindow?.RefreshDataContext(2);
            RenderGraph(2);
            SetStatus("Готов");
            UpdateStatusBarFileInfo();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            schematicWindow?.RefreshDataContext(2);
            Graph2Control.ClearSeries("Ошибка загрузки");
            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo();
        }
    }

    private void Graph2LinkedModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        chartWorkspace.SetGraph2Mode(Graph2DataMode.LinkedToGraph1);
        schematicWindow?.RefreshDataContext(2);
        UpdateGraph2DataModeUi();
        RenderGraph(2);
        UpdateStatusBarFileInfo();
    }

    private void Graph2IndependentModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        chartWorkspace.SetGraph2Mode(Graph2DataMode.IndependentFile);
        schematicWindow?.RefreshDataContext(2);
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

    private void Graph1SchematicButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSchematicWindow(1);
    }

    private void Graph2SchematicButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSchematicWindow(2);
    }

    private void ShowSchematicWindow(int graphNumber)
    {
        try
        {
            schematicWindow ??= new SchematicWindow(chartWorkspace, curveSelectionService, OnCurveConfigurationApplied);
            schematicWindow.ShowForGraph(graphNumber, WindowNative.GetWindowHandle(this));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "amk-schematic-window-error.txt"), exception.ToString());
            throw;
        }
    }

    private void OnCurveConfigurationApplied(int graphNumber)
    {
        RenderGraph(graphNumber);
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

        ConfiguredChartData chartData = ChartDataCalculator.CalculateChartDataFromConfiguration(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration);
        List<float> xAxisData = ChartDataCalculator.CalculateXAxisData(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration.XAxisType);
        GroupedCurves groupedCurves = ChartDataCalculator.GroupCurvesByAxis(graphConfiguration.Curves);
        List<ChartRenderSeries> renderSeries = [];

        for (int index = 0; index < Math.Min(groupedCurves.LeftAxisCurves.Count, chartData.LeftAxisData.Count); index++)
        {
            CurveDefinition curve = groupedCurves.LeftAxisCurves[index];
            renderSeries.Add(new ChartRenderSeries(
                chartData.LeftAxisData[index],
                curve.DisplayName,
                curve.Units,
                curve.Color,
                curve.YAxisGroup));
        }

        for (int index = 0; index < Math.Min(groupedCurves.RightAxisCurves.Count, chartData.RightAxisData.Count); index++)
        {
            CurveDefinition curve = groupedCurves.RightAxisCurves[index];
            renderSeries.Add(new ChartRenderSeries(
                chartData.RightAxisData[index],
                curve.DisplayName,
                curve.Units,
                curve.Color,
                curve.YAxisGroup));
        }

        graphControl.SetSeries(
            renderSeries,
            $"График {graphNumber}",
            graphConfiguration.LeftAxisUnits,
            graphConfiguration.RightAxisUnits,
            xAxisData,
            graphConfiguration.XAxisType == XAxisType.Temperature ? "Температура, °C" : "Номер кадра",
            graphConfiguration.IsPresetActive ? graphConfiguration.PresetDisplayName : string.Empty);
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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        schematicWindow?.ClosePermanently();
        schematicWindow = null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
