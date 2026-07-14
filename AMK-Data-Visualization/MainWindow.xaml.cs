using AMK.DataVisualization.App.Controls;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.IO.DataFiles;
using AMK.DataVisualization.Hardware.Serial;
using AMK.DataVisualization.App.Layout;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private readonly CurveSelectionService curveSelectionService = new();
    private readonly ChartWorkspace chartWorkspace = new(new ChartConfiguration(), new ChartConfiguration());
    private readonly SerialDeviceService serialDeviceService = new();
    private readonly SerialConnectionSettingsStore serialSettingsStore = new();
    private SchematicWindow? schematicWindow;
    private ConnectionSettingsWindow? connectionSettingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        ShowLegacyEmptyState(1, hasData: false);
        ShowLegacyEmptyState(2, hasData: false);
        UpdateDualModeLayout();
        serialDeviceService.FrameReceived += SerialDeviceService_FrameReceived;
        serialDeviceService.StatusChanged += SerialDeviceService_StatusChanged;
        UpdateConnectionControls(serialDeviceService.GetStatus());
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

    private void ConnectionSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowConnectionSettingsWindow();
    }

    private void ShowConnectionSettingsWindow()
    {
        connectionSettingsWindow ??= new ConnectionSettingsWindow(
            serialDeviceService,
            serialSettingsStore,
            PrepareSerialSession);
        connectionSettingsWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    private async void ConnectionToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (serialDeviceService.IsConnected)
        {
            await serialDeviceService.DisconnectAsync();
            SetStatus("Прибор отключён");
            return;
        }

        SerialConnectionSettings settings = serialSettingsStore.Load();
        IReadOnlyList<string> ports = await Task.Run(SerialDeviceService.GetAvailablePorts);
        if (string.IsNullOrWhiteSpace(settings.LastPort)
            || !ports.Contains(settings.LastPort, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus("Выберите COM-порт в настройках подключения");
            ShowConnectionSettingsWindow();
            return;
        }

        try
        {
            ConnectionToggleButton.IsEnabled = false;
            PrepareSerialSession(settings);
            serialDeviceService.ConfigureRecording(
                settings.RecordingEnabled,
                settings.RecordFolder,
                settings.AppendToExistingFile ? settings.ExistingRecordFile : string.Empty);
            SerialOperationResult result = await serialDeviceService.ConnectAsync(settings.LastPort);
            if (result == SerialOperationResult.Success)
            {
                await serialDeviceService.StartPollingAsync(settings.PollIntervalMilliseconds);
                SetStatus($"Опрос {settings.LastPort} запущен");
            }
            else
            {
                SetStatus(SerialDeviceService.ResultToDisplayText(result));
                ShowConnectionSettingsWindow();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus(exception.Message);
            ShowConnectionSettingsWindow();
        }
        finally
        {
            UpdateConnectionControls(serialDeviceService.GetStatus());
        }
    }

    private async void PollingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!serialDeviceService.IsConnected)
        {
            ShowConnectionSettingsWindow();
            return;
        }

        if (serialDeviceService.IsPolling)
        {
            await serialDeviceService.StopPollingAsync();
            SetStatus("Опрос прибора приостановлен");
        }
        else
        {
            SerialConnectionSettings settings = serialSettingsStore.Load();
            await serialDeviceService.StartPollingAsync(settings.PollIntervalMilliseconds);
            SetStatus("Опрос прибора продолжен");
        }

        UpdateConnectionControls(serialDeviceService.GetStatus());
    }

    private void PrepareSerialSession(SerialConnectionSettings settings)
    {
        bool loadExisting = settings.AppendToExistingFile
            && settings.ShowFullExistingFile
            && File.Exists(settings.ExistingRecordFile);
        if (loadExisting)
        {
            chartWorkspace.Graph1Repository.LoadDataFromFile(settings.ExistingRecordFile);
        }
        else
        {
            chartWorkspace.Graph1Repository.ClearData();
        }

        schematicWindow?.RefreshDataContext(1, 2);
        RenderGraph(1);
        if (!chartWorkspace.IsGraph2Independent)
        {
            RenderGraph(2);
        }

        UpdateStatusBarFileInfo();
    }

    private void SerialDeviceService_FrameReceived(object? sender, SerialFrameReceivedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            bool firstFrame = !chartWorkspace.Graph1Repository.HasData;
            chartWorkspace.Graph1Repository.AddParsedData(e.Frame);
            if (firstFrame)
            {
                schematicWindow?.RefreshDataContext(1, 2);
            }

            RenderGraph(1);
            if (!chartWorkspace.IsGraph2Independent && DualModeCheckBox.IsChecked == true)
            {
                RenderGraph(2);
            }

            SetStatus($"Получен кадр {e.Frame.CanonicalData.Frame}");
            UpdateStatusBarFileInfo();
        });
    }

    private void SerialDeviceService_StatusChanged(object? sender, SerialStatusChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateConnectionControls(e.Status));
    }

    private void UpdateConnectionControls(SerialStatusSnapshot status)
    {
        bool connected = status.State == SerialConnectionState.Connected;
        bool busy = status.State == SerialConnectionState.Connecting;
        ConnectionToggleButton.Content = connected ? "Отключить" : "Включить";
        ConnectionToggleButton.IsEnabled = !busy;
        PollingToggleButton.Content = status.IsPolling ? "⏸" : "▶";
        PollingToggleButton.IsEnabled = connected;
        if (status.State == SerialConnectionState.ConnectionError && !string.IsNullOrWhiteSpace(status.LastError))
        {
            SetStatus(status.LastError);
        }

        UpdateStatusBarFileInfo();
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
                graphConfiguration.IsPresetActive ? GetPresetLegendLabel(curve) : GetCombinedLegendLabel(curve),
                GetLegendCategory(curve),
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
                graphConfiguration.IsPresetActive ? GetPresetLegendLabel(curve) : GetCombinedLegendLabel(curve),
                GetLegendCategory(curve),
                curve.Units,
                curve.Color,
                curve.YAxisGroup));
        }

        graphControl.SetSeries(
            renderSeries,
            $"График {graphNumber} ({GetFrequencyDisplayName(graphConfiguration)})",
            graphConfiguration.LeftAxisUnits,
            graphConfiguration.RightAxisUnits,
            xAxisData,
            graphConfiguration.XAxisType == XAxisType.Temperature ? "Температура, °C" : "Номер кадра",
            graphConfiguration.IsPresetActive ? graphConfiguration.PresetDisplayName : string.Empty);
    }

    private static string GetPresetLegendLabel(CurveDefinition curve)
    {
        return curve.DataType switch
        {
            DataType.Temperature => "Temp",
            DataType.SilentAmplitudeRx1 => "Молч. R1",
            DataType.SilentAmplitudeRx2 => "Молч. R2",
            DataType.SilentPhaseDifference => "Молч. R1-R2",
            _ when curve.SourceElement == ElementType.Transmitter => $"T{curve.ElementIndex + 1}",
            _ when curve.SourceElement == ElementType.Receiver => $"R{curve.ElementIndex + 1}",
            _ => curve.DisplayName
        };
    }

    private static string GetCombinedLegendLabel(CurveDefinition curve)
    {
        return curve.SourceElement switch
        {
            ElementType.Transmitter => curve.DataType switch
            {
                DataType.AmplitudeRx1 => $"T{curve.ElementIndex + 1} R1",
                DataType.AmplitudeRx2 => $"T{curve.ElementIndex + 1} R2",
                _ => $"T{curve.ElementIndex + 1}"
            },
            ElementType.Receiver => $"R{curve.ElementIndex + 1}",
            ElementType.SilentTx when curve.DataType == DataType.SilentPhaseDifference => "R1-R2 (молч.)",
            ElementType.SilentTx => $"R{curve.ElementIndex + 1} (молч.)",
            _ => curve.DisplayName
        };
    }

    private static string GetLegendCategory(CurveDefinition curve)
    {
        return curve.DataType switch
        {
            DataType.PhaseDifference or DataType.SilentPhaseDifference => "Разность фаз",
            DataType.PhaseSmt => "Фаза",
            DataType.AmplitudeRx1 or DataType.AmplitudeRx2 or
                DataType.SilentAmplitudeRx1 or DataType.SilentAmplitudeRx2 => "Амплитуда",
            DataType.AmplitudeRatio => "Отношение амплитуд",
            DataType.RhoSmt => "УЭС",
            DataType.Temperature => "Температура",
            _ => string.Empty
        };
    }

    private static string GetFrequencyDisplayName(ChartConfiguration configuration)
    {
        CurveDefinition? firstEnabledCurve = configuration.Curves.FirstOrDefault(curve => curve.Enabled);
        return firstEnabledCurve?.Frequency == FrequencyBand.Freq2000KHz ? "2000 кГц" : "400 кГц";
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
        bool connected = serialDeviceService.IsConnected;
        FolderStatusIcon.Visibility = hasAnyData || connected ? Visibility.Visible : Visibility.Collapsed;
        LoadedFileStatusTextBlock.Text = connected
            ? $"{serialDeviceService.ConnectedPort}  |  Кадров: {chartWorkspace.Graph1Repository.DataSize}"
            : chartWorkspace.GetStatusFileInformation();
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
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        serialDeviceService.FrameReceived -= SerialDeviceService_FrameReceived;
        serialDeviceService.StatusChanged -= SerialDeviceService_StatusChanged;
        connectionSettingsWindow?.ClosePermanently();
        connectionSettingsWindow = null;
        schematicWindow?.ClosePermanently();
        schematicWindow = null;
        serialDeviceService.Dispose();
    }
}
