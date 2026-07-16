using AMK.DataVisualization.App.Controls;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.IO.DataFiles;
using AMK.DataVisualization.Hardware.Serial;
using AMK.DataVisualization.Metrology;
using AMK.DataVisualization.App.Layout;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private readonly CurveSelectionService curveSelectionService = new();
    private readonly ChartWorkspace chartWorkspace = new(new ChartConfiguration(), new ChartConfiguration());
    private readonly SerialDeviceService serialDeviceService = new();
    private readonly SerialConnectionSettingsStore serialSettingsStore = new();
    private readonly MetrologySession metrologySession = new();
    private readonly AutoUpdateService autoUpdateService = new();
    private readonly CancellationTokenSource autoUpdateCancellation = new();
    private SchematicWindow? schematicWindow;
    private ConnectionSettingsWindow? connectionSettingsWindow;
    private MetrologyWindow? metrologyWindow;
    private bool initialMaximizeApplied;
    private bool updatingRangeControls;
    private bool startupUpdateCheckScheduled;
    private bool updateCheckInProgress;

    public MainWindow()
    {
        InitializeComponent();
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DataVisualization.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        GraphControl.ZoomRequested += (_, args) => ApplyPointerZoom(1, args);
        Graph2Control.ZoomRequested += (_, args) => ApplyPointerZoom(2, args);
        GraphControl.HorizontalScrollRequested += (_, args) => ScrollGraphX(1, args);
        Graph2Control.HorizontalScrollRequested += (_, args) => ScrollGraphX(2, args);
        GraphControl.VerticalScrollRequested += (_, args) => ScrollGraphY(1, args);
        Graph2Control.VerticalScrollRequested += (_, args) => ScrollGraphY(2, args);
        ShowLegacyEmptyState(1, hasData: false);
        ShowLegacyEmptyState(2, hasData: false);
        UpdateDualModeLayout();
        serialDeviceService.FrameReceived += SerialDeviceService_FrameReceived;
        serialDeviceService.StatusChanged += SerialDeviceService_StatusChanged;
        UpdateConnectionControls(serialDeviceService.GetStatus());
        Activated += MainWindow_Activated;
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
            chartWorkspace.Graph1Viewport.ResetAll();
            if (!chartWorkspace.IsGraph2Independent)
            {
                chartWorkspace.Graph2Viewport.ResetAll();
            }
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

    private void MetrologyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            metrologyWindow ??= new MetrologyWindow(serialDeviceService, metrologySession, OnMetrologyUpdated);
            metrologyWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
        }
        catch (Exception exception)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "amk-metrology-window-error.txt"), exception + Environment.NewLine);
            throw;
        }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUpdateCheckAsync(silent: false, autoUpdateCancellation.Token);
    }

    private async Task CheckForUpdatesAfterStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), autoUpdateCancellation.Token);
            await RunUpdateCheckAsync(silent: true, autoUpdateCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunUpdateCheckAsync(bool silent, CancellationToken cancellationToken)
    {
        if (updateCheckInProgress)
        {
            return;
        }

        updateCheckInProgress = true;
        UpdateCheckButton.IsEnabled = false;
        if (!silent)
        {
            SetStatus("Проверка обновлений...");
        }

        try
        {
            UpdateCheckResult update = await autoUpdateService.CheckForUpdateAsync(cancellationToken);
            if (!update.IsSuccessful)
            {
                if (!silent)
                {
                    await ShowUpdateMessageAsync("Проверка обновлений", update.ErrorMessage);
                    SetStatus("Не удалось проверить обновления");
                }

                return;
            }

            if (!update.IsAvailable)
            {
                if (!silent)
                {
                    string current = AutoUpdateService.FormatVersion(update.CurrentVersion);
                    await ShowUpdateMessageAsync(
                        "Проверка обновлений",
                        $"Установлена последняя версия ({current}).");
                    SetStatus("Установлена последняя версия");
                }

                return;
            }

            string currentVersion = AutoUpdateService.FormatVersion(update.CurrentVersion);
            string remoteVersion = AutoUpdateService.FormatVersion(update.RemoteVersion);
            ContentDialog prompt = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = "Обновление доступно",
                Content = $"Доступна версия {remoteVersion}.\nТекущая версия: {currentVersion}.\n\nСкачать и установить обновление?",
                PrimaryButtonText = "Скачать и установить",
                CloseButtonText = "Позже",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await prompt.ShowAsync() != ContentDialogResult.Primary)
            {
                SetStatus("Обновление отложено");
                return;
            }

            SetStatus($"Загрузка обновления {remoteVersion}...");
            string installerPath = await autoUpdateService.DownloadInstallerAsync(update, cancellationToken);
            if (!AutoUpdateService.StartInstaller(installerPath))
            {
                await ShowUpdateMessageAsync(
                    "Ошибка обновления",
                    "Установщик скачан, но его не удалось запустить.");
                SetStatus("Не удалось запустить установщик обновления");
                return;
            }

            SetStatus("Установщик обновления запущен");
            Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException
                                           or InvalidDataException
                                           or HttpRequestException
                                           or UnauthorizedAccessException
                                           or System.ComponentModel.Win32Exception)
        {
            if (!silent || !autoUpdateCancellation.IsCancellationRequested)
            {
                await ShowUpdateMessageAsync("Ошибка обновления", exception.Message);
                SetStatus("Ошибка обновления");
            }
        }
        finally
        {
            updateCheckInProgress = false;
            UpdateCheckButton.IsEnabled = true;
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
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

        chartWorkspace.Graph1Viewport.ResetAll();
        if (!chartWorkspace.IsGraph2Independent)
        {
            chartWorkspace.Graph2Viewport.ResetAll();
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

        metrologyWindow?.RefreshConnectionState();

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
            chartWorkspace.Graph2Viewport.ResetAll();
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
        chartWorkspace.Graph2Viewport.ResetAll();
        schematicWindow?.RefreshDataContext(2);
        UpdateGraph2DataModeUi();
        RenderGraph(2);
        UpdateStatusBarFileInfo();
    }

    private void Graph2IndependentModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        chartWorkspace.SetGraph2Mode(Graph2DataMode.IndependentFile);
        chartWorkspace.Graph2Viewport.ResetAll();
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
            schematicWindow ??= new SchematicWindow(chartWorkspace, curveSelectionService, metrologySession, OnCurveConfigurationApplied);
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

    private void OnMetrologyUpdated()
    {
        schematicWindow?.RefreshMetrologyAvailability();
        RenderGraph(1);
        if (DualModeCheckBox.IsChecked == true)
        {
            RenderGraph(2);
        }

        SetStatus("Метрология доступна для расчёта УЭС и симметризованных фаз");
    }

    private void RenderGraph(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        var graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        ChartConfiguration graphConfiguration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);

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

        ChartIndexRange visibleRange = viewport.GetVisibleRange(repository.DataSize);
        ConfiguredChartData chartData = ChartDataCalculator.CalculateChartDataFromConfiguration(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration,
            visibleRange.StartIndex,
            visibleRange.EndIndex,
            -1e12,
            1e12,
            metrologySession.Current);
        List<float> allXAxisData = ChartDataCalculator.CalculateXAxisData(
            repository.Records,
            repository.RecordMetadata,
            graphConfiguration.XAxisType);
        List<float> xAxisData = allXAxisData
            .Skip(visibleRange.StartIndex)
            .Take(visibleRange.EndIndex - visibleRange.StartIndex + 1)
            .ToList();
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
            graphConfiguration.IsPresetActive ? graphConfiguration.PresetDisplayName : string.Empty,
            viewport.LeftYRange,
            viewport.RightYRange);
        graphControl.IsFlowMode = viewport.IsFlowEnabled;
        graphControl.ConfigureScrollBars(
            repository.DataSize,
            visibleRange.StartIndex,
            visibleRange.EndIndex,
            !viewport.IsFlowEnabled && viewport.HasCustomXRange);
        UpdateRangeControls(graphNumber);
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

    private void Graph1ApplyRangeButton_Click(object sender, RoutedEventArgs e) => ApplyTextRange(1);

    private void Graph2ApplyRangeButton_Click(object sender, RoutedEventArgs e) => ApplyTextRange(2);

    private void ApplyTextRange(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        (string from, string to) = GetRangeText(graphNumber);
        if (!chartWorkspace.GetViewportForGraph(graphNumber).TryApplyXRange(repository.DataSize, from, to, out string error))
        {
            SetStatus(error);
            return;
        }

        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: выбран участок {from}–{(string.IsNullOrWhiteSpace(to) ? repository.DataSize - 1 : to)}");
    }

    private void Graph1ResetRangeButton_Click(object sender, RoutedEventArgs e) => ResetRange(1);

    private void Graph2ResetRangeButton_Click(object sender, RoutedEventArgs e) => ResetRange(2);

    private void ResetRange(int graphNumber)
    {
        chartWorkspace.GetViewportForGraph(graphNumber).ResetRange();
        SetRangeText(graphNumber, string.Empty, string.Empty);
        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: диапазон сброшен");
    }

    private void Graph1ResetZoomButton_Click(object sender, RoutedEventArgs e) => ResetZoom(1);

    private void Graph2ResetZoomButton_Click(object sender, RoutedEventArgs e) => ResetZoom(2);

    private void Graph1UndoZoomButton_Click(object sender, RoutedEventArgs e) => UndoZoom(1);

    private void Graph2UndoZoomButton_Click(object sender, RoutedEventArgs e) => UndoZoom(2);

    private void UndoZoom(int graphNumber)
    {
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        if (!viewport.TryUndoZoom())
        {
            return;
        }

        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: отменён последний zoom");
    }

    private void ResetZoom(int graphNumber)
    {
        chartWorkspace.GetViewportForGraph(graphNumber).ResetZoom();
        SetRangeText(graphNumber, string.Empty, string.Empty);
        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: zoom сброшен");
    }

    private void Chart1SurfCheckBox_Changed(object sender, RoutedEventArgs e) => ChangeFlowMode(1);

    private void Chart2SurfCheckBox_Changed(object sender, RoutedEventArgs e) => ChangeFlowMode(2);

    private void Chart1SurfTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Chart1SurfCheckBox.IsChecked == true)
        {
            ChangeFlowMode(1);
        }
    }

    private void Chart2SurfTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Chart2SurfCheckBox.IsChecked == true)
        {
            ChangeFlowMode(2);
        }
    }

    private void ChangeFlowMode(int graphNumber)
    {
        if (updatingRangeControls)
        {
            return;
        }

        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        bool enabled = graphNumber == 1 ? Chart1SurfCheckBox.IsChecked == true : Chart2SurfCheckBox.IsChecked == true;
        string windowText = graphNumber == 1 ? Chart1SurfTextBox.Text : Chart2SurfTextBox.Text;
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        if (!viewport.TrySetFlow(enabled, windowText, repository.DataSize, out string error))
        {
            updatingRangeControls = true;
            if (graphNumber == 1)
            {
                Chart1SurfCheckBox.IsChecked = false;
            }
            else
            {
                Chart2SurfCheckBox.IsChecked = false;
            }
            updatingRangeControls = false;
            SetStatus(error);
            return;
        }

        SetRangeText(graphNumber, string.Empty, string.Empty);
        RenderGraph(graphNumber);
        SetStatus(enabled ? $"График {graphNumber}: Поток ON ({viewport.FlowWindowSize})" : $"График {graphNumber}: Поток OFF");
    }

    private void ApplyPointerZoom(int graphNumber, ChartZoomRequestedEventArgs args)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        if (!viewport.ApplyDragZoom(repository.DataSize, args.StartFraction, args.EndFraction, args.LeftYRange, args.RightYRange))
        {
            SetStatus(viewport.IsFlowEnabled
                ? "Минимальный диапазон Y — 10 единиц"
                : "Минимальный диапазон X — 10 записей");
            return;
        }
        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: zoom применён");
    }

    private void ScrollGraphX(int graphNumber, ChartXScrollRequestedEventArgs args)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        if (!viewport.TryScrollXTo(repository.DataSize, args.StartIndex))
        {
            return;
        }

        ChartIndexRange visibleRange = viewport.GetVisibleRange(repository.DataSize);
        SetRangeText(
            graphNumber,
            visibleRange.StartIndex.ToString(),
            visibleRange.EndIndex.ToString());
        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: участок {visibleRange.StartIndex}–{visibleRange.EndIndex}");
    }

    private void ScrollGraphY(int graphNumber, ChartYScrollRequestedEventArgs args)
    {
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        if (!viewport.TrySetScrolledYRanges(args.LeftYRange, args.RightYRange))
        {
            return;
        }

        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: прокрутка по оси Y");
    }

    private (string From, string To) GetRangeText(int graphNumber)
    {
        return graphNumber == 1
            ? (XRangeFromTextBox.Text, XRangeToTextBox.Text)
            : (XRange2FromTextBox.Text, XRange2ToTextBox.Text);
    }

    private void SetRangeText(int graphNumber, string from, string to)
    {
        if (graphNumber == 1)
        {
            XRangeFromTextBox.Text = from;
            XRangeToTextBox.Text = to;
        }
        else
        {
            XRange2FromTextBox.Text = from;
            XRange2ToTextBox.Text = to;
        }
    }

    private void UpdateRangeControls(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        bool hasData = repository.HasData;
        updatingRangeControls = true;
        if (graphNumber == 1)
        {
            Graph1ApplyRangeButton.IsEnabled = hasData && !viewport.IsFlowEnabled;
            Graph1ResetRangeButton.IsEnabled = hasData && viewport.HasCustomXRange;
            Graph1SaveRangeButton.IsEnabled = hasData;
            Graph1CutRangeButton.IsEnabled = hasData && !string.IsNullOrWhiteSpace(repository.LoadedFilePath);
            Graph1ResetZoomButton.IsEnabled = hasData && viewport.IsZoomed;
            Graph1UndoZoomButton.IsEnabled = hasData && viewport.CanUndoZoom;
            Chart1SurfCheckBox.IsChecked = viewport.IsFlowEnabled;
            Chart1SurfTextBox.Text = viewport.FlowWindowSize.ToString();
        }
        else
        {
            Graph2ApplyRangeButton.IsEnabled = hasData && !viewport.IsFlowEnabled;
            Graph2ResetRangeButton.IsEnabled = hasData && viewport.HasCustomXRange;
            Graph2ResetZoomButton.IsEnabled = hasData && viewport.IsZoomed;
            Graph2UndoZoomButton.IsEnabled = hasData && viewport.CanUndoZoom;
            Chart2SurfCheckBox.IsChecked = viewport.IsFlowEnabled;
            Chart2SurfTextBox.Text = viewport.FlowWindowSize.ToString();
        }
        updatingRangeControls = false;
    }

    private async void Graph1SaveRangeButton_Click(object sender, RoutedEventArgs e)
    {
        ToolDataRepository repository = chartWorkspace.Graph1Repository;
        if (!TryResolveGraph1OperationRange(out ChartIndexRange range))
        {
            SetStatus("Не удалось определить выбранный участок");
            return;
        }

        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = $"{repository.LoadedFileName}_range_{range.StartIndex}_{range.EndIndex}_{DateTime.Now:dd_MM_yyyy}"
        };
        picker.FileTypeChoices.Add("AMK BIN", [".bin"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        bool saved = await Task.Run(() => repository.SaveDataRange(file.Path, range.StartIndex, range.EndIndex));
        SetStatus(saved ? $"Сохранён участок {range.StartIndex}–{range.EndIndex}" : "Не удалось сохранить выбранный участок");
    }

    private async void Graph1CutRangeButton_Click(object sender, RoutedEventArgs e)
    {
        ToolDataRepository repository = chartWorkspace.Graph1Repository;
        if (!TryResolveGraph1OperationRange(out ChartIndexRange range))
        {
            SetStatus("Не удалось определить выбранный участок");
            return;
        }

        if (range.StartIndex == 0 && range.EndIndex == repository.DataSize - 1)
        {
            SetStatus("Нельзя вырезать весь файл");
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Вырезание выбранного участка",
            Content = $"Удалить записи {range.StartIndex}–{range.EndIndex} из текущего файла или сохранить копию без них?",
            PrimaryButtonText = "Из текущего файла",
            SecondaryButtonText = "Сохранить копию",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };
        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None)
        {
            return;
        }

        if (choice == ContentDialogResult.Primary)
        {
            await CutRangeFromCurrentFile(repository, range);
            return;
        }

        string extension = Path.GetExtension(repository.LoadedFilePath);
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = $"{repository.LoadedFileName}_cut_{range.StartIndex}_{range.EndIndex}"
        };
        picker.FileTypeChoices.Add("AMK data", [extension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? target = await picker.PickSaveFileAsync();
        if (target is null)
        {
            return;
        }

        bool saved = await Task.Run(() => ToolDataRangeExporter.SaveFileExcludingRange(
            repository.LoadedFilePath, target.Path, range.StartIndex, range.EndIndex));
        SetStatus(saved ? "Копия без выбранного участка сохранена" : "Не удалось сохранить копию");
    }

    private async Task CutRangeFromCurrentFile(ToolDataRepository repository, ChartIndexRange range)
    {
        string source = repository.LoadedFilePath;
        string extension = Path.GetExtension(source);
        string temporary = Path.Combine(Path.GetDirectoryName(source)!, $".{Path.GetFileNameWithoutExtension(source)}.cut-{Guid.NewGuid():N}{extension}");
        try
        {
            bool created = await Task.Run(() => ToolDataRangeExporter.SaveFileExcludingRange(source, temporary, range.StartIndex, range.EndIndex));
            if (!created)
            {
                SetStatus("Не удалось подготовить файл без выбранного участка");
                return;
            }

            _ = ToolDataFileReader.LoadDataFromFile(temporary);
            File.Move(temporary, source, true);
            repository.LoadDataFromFile(source);
            chartWorkspace.Graph1Viewport.ResetAll();
            if (!chartWorkspace.IsGraph2Independent)
            {
                chartWorkspace.Graph2Viewport.ResetAll();
            }
            RenderGraph(1);
            if (!chartWorkspace.IsGraph2Independent)
            {
                RenderGraph(2);
            }
            UpdateStatusBarFileInfo();
            SetStatus("Выбранный участок вырезан из текущего файла");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus($"Ошибка вырезания: {ex.Message}");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private bool TryResolveGraph1OperationRange(out ChartIndexRange range)
    {
        (string from, string to) = GetRangeText(1);
        return chartWorkspace.Graph1Viewport.TryResolveOperationRange(
            chartWorkspace.Graph1Repository.DataSize, from, to, out range);
    }

    private void ShowLegacyEmptyState(int graphNumber, bool hasData)
    {
        var graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        graphControl.ShowLegacyEmptyState(
            $"График {graphNumber}",
            hasData
                ? "Нажмите 'Схема прибора' для настройки графика"
                : "Сначала откройте файл данных");
        UpdateRangeControls(graphNumber);
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

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (initialMaximizeApplied || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        initialMaximizeApplied = true;
        MaximizeLikeLegacyApplication();
        if (!startupUpdateCheckScheduled)
        {
            startupUpdateCheckScheduled = true;
            _ = CheckForUpdatesAfterStartupAsync();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        serialDeviceService.FrameReceived -= SerialDeviceService_FrameReceived;
        serialDeviceService.StatusChanged -= SerialDeviceService_StatusChanged;
        Activated -= MainWindow_Activated;
        autoUpdateCancellation.Cancel();
        connectionSettingsWindow?.ClosePermanently();
        connectionSettingsWindow = null;
        metrologyWindow?.ClosePermanently();
        metrologyWindow = null;
        schematicWindow?.ClosePermanently();
        schematicWindow = null;
        serialDeviceService.Dispose();
        autoUpdateService.Dispose();
        autoUpdateCancellation.Dispose();
    }
}
