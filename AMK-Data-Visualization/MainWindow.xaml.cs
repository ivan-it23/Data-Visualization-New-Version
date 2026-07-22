using AMK.DataVisualization.App.Controls;
using AMK.DataVisualization.Charting.Notes;
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
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;
using System.Globalization;
using System.Diagnostics;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private sealed record FolderTarget(string Label, string FolderPath);

    private readonly CurveSelectionService curveSelectionService = new();
    private readonly ChartWorkspace chartWorkspace = new(new ChartConfiguration(), new ChartConfiguration());
    private readonly SerialDeviceService serialDeviceService = new();
    private readonly SerialConnectionSettingsStore serialSettingsStore = new();
    private readonly MetrologySession metrologySession = new();
    private readonly AutoUpdateService autoUpdateService = new();
    private readonly NotesModule notesModule = new();
    private readonly CancellationTokenSource autoUpdateCancellation = new();
    private SchematicWindow? schematicWindow;
    private ConnectionSettingsWindow? connectionSettingsWindow;
    private MetrologyWindow? metrologyWindow;
    private FileConstructorWindow? fileConstructorWindow;
    private NotesJournalWindow? notesJournalWindow;
    private string currentDataFilePath = string.Empty;
    private string configuredAppendRecordPath = string.Empty;
    private bool serialRecordingEnabled;
    private int activeChartNumber = 1;
    private ChartPointSelectedEventArgs? chart1Crosshair;
    private ChartPointSelectedEventArgs? chart2Crosshair;
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
        GraphControl.PointSelected += (_, args) => ChartPointSelected(1, args);
        Graph2Control.PointSelected += (_, args) => ChartPointSelected(2, args);
        GraphControl.NoteMarkerActivated += async (_, args) => await OpenNoteAsync(1, args.NoteId);
        Graph2Control.NoteMarkerActivated += async (_, args) => await OpenNoteAsync(2, args.NoteId);
        GraphControl.ChartContextRequested += (_, args) => ShowChartContextMenu(1, GraphControl, args);
        Graph2Control.ChartContextRequested += (_, args) => ShowChartContextMenu(2, Graph2Control, args);
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
            if (fileConstructorWindow is not null)
            {
                await fileConstructorWindow.LoadOpenedDataFileAsync(file.Path);
            }

            currentDataFilePath = file.Path;
            EnsureNotesLoadedForCurrentSource();

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

    private async void FileConstructorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        bool windowCreated = fileConstructorWindow is null;
        fileConstructorWindow ??= new FileConstructorWindow(OnFileConstructorSaved);
        fileConstructorWindow.ShowForOwner(WindowNative.GetWindowHandle(this));

        string currentFilePath = chartWorkspace.Graph1Repository.LoadedFilePath;
        if (windowCreated && !string.IsNullOrWhiteSpace(currentFilePath))
        {
            await fileConstructorWindow.LoadOpenedDataFileAsync(currentFilePath);
        }
    }

    private void OnFileConstructorSaved(string filePath)
    {
        try
        {
            ToolDataRepository repository = chartWorkspace.Graph1Repository;
            repository.LoadDataFromFile(filePath);
            currentDataFilePath = filePath;
            EnsureNotesLoadedForCurrentSource();
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
            SetStatus("Сконструированный файл сохранён и загружен");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            SetStatus($"Файл сохранён, но не удалось обновить график: {exception.Message}");
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
                        $"Установлена последняя версия ({current}).",
                        compactCentered: true);
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

    private async Task ShowUpdateMessageAsync(
        string title,
        string message,
        bool compactCentered = false)
    {
        TextBlock messageText = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = compactCentered ? TextAlignment.Center : TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Button closeButton = new()
        {
            Content = "Закрыть",
            Width = compactCentered ? 180 : 220,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        closeButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];

        StackPanel content = new()
        {
            Width = compactCentered ? 360 : 500,
            Spacing = compactCentered ? 14 : 24
        };
        if (compactCentered)
        {
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        content.Children.Add(messageText);
        content.Children.Add(closeButton);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Content = content
        };
        if (compactCentered)
        {
            dialog.Resources["ContentDialogMinHeight"] = 0d;
            dialog.Resources["ContentDialogPadding"] = new Thickness(24, 18, 24, 12);
        }
        else
        {
            dialog.Title = title;
        }

        closeButton.Click += (_, _) => dialog.Hide();
        dialog.Opened += (_, _) => closeButton.Focus(FocusState.Programmatic);
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
        currentDataFilePath = string.Empty;
        serialRecordingEnabled = settings.RecordingEnabled;
        configuredAppendRecordPath = settings.RecordingEnabled
            && settings.AppendToExistingFile
            && File.Exists(settings.ExistingRecordFile)
                ? settings.ExistingRecordFile
                : string.Empty;
        EnsureNotesLoadedForCurrentSource();
        GraphControl.ClearCrosshair();
        Graph2Control.ClearCrosshair();
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
        DispatcherQueue.TryEnqueue(() =>
        {
            bool notesSourceChanged = EnsureNotesLoadedForCurrentSource();
            UpdateConnectionControls(e.Status);
            if (notesSourceChanged)
            {
                RenderGraph(1);
                if (!chartWorkspace.IsGraph2Independent && DualModeCheckBox.IsChecked == true)
                {
                    RenderGraph(2);
                }
            }
        });
    }

    private void UpdateConnectionControls(SerialStatusSnapshot status)
    {
        bool connected = status.State == SerialConnectionState.Connected;
        bool busy = status.State == SerialConnectionState.Connecting;
        ConnectionToggleButton.Content = connected ? "Отключить" : "Включить";
        ConnectionToggleButton.IsEnabled = !busy;
        PollingToggleIcon.Symbol = status.IsPolling ? Symbol.Pause : Symbol.Play;
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

    private async void FolderStatusButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<FolderTarget> targets = GetStatusFolderTargets();
        if (targets.Count == 0)
        {
            await ShowOperationMessageAsync("Папка", "Папка файла данных недоступна.");
            return;
        }

        FolderTarget? selectedTarget = targets.Count == 1
            ? targets[0]
            : await SelectStatusFolderTargetAsync(targets);
        if (selectedTarget is null)
        {
            return;
        }

        await OpenStatusFolderAsync(selectedTarget);
    }

    private IReadOnlyList<FolderTarget> GetStatusFolderTargets()
    {
        List<FolderTarget> targets = new(2);
        FolderTarget? graph1Target = serialDeviceService.IsConnected
            ? CreateSerialRecordingFolderTarget()
            : CreateRepositoryFolderTarget(1, chartWorkspace.Graph1Repository);
        if (graph1Target is not null)
        {
            targets.Add(graph1Target);
        }

        bool hasIndependentGraph2 = DualModeCheckBox.IsChecked == true
            && chartWorkspace.IsGraph2Independent;
        if (hasIndependentGraph2)
        {
            FolderTarget? graph2Target = CreateRepositoryFolderTarget(
                2,
                chartWorkspace.Graph2IndependentRepository);
            if (graph2Target is not null)
            {
                targets.Add(graph2Target);
            }
        }

        return targets;
    }

    private FolderTarget? CreateSerialRecordingFolderTarget()
    {
        SerialStatusSnapshot status = serialDeviceService.GetStatus();
        SerialConnectionSettings settings = serialSettingsStore.Load();
        string recordFilePath = status.RecordFilePath;
        if (string.IsNullOrWhiteSpace(recordFilePath) && settings.AppendToExistingFile)
        {
            recordFilePath = settings.ExistingRecordFile;
        }

        string folderPath = !string.IsNullOrWhiteSpace(recordFilePath)
            ? Path.GetDirectoryName(recordFilePath) ?? string.Empty
            : settings.RecordFolder;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        string sourceLabel = !string.IsNullOrWhiteSpace(recordFilePath)
            ? Path.GetFileName(recordFilePath)
            : $"запись {status.PortName}";
        return new FolderTarget($"График 1 — {sourceLabel}", folderPath);
    }

    private static FolderTarget? CreateRepositoryFolderTarget(
        int graphNumber,
        ToolDataRepository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.LoadedFilePath))
        {
            return null;
        }

        string folderPath = Path.GetDirectoryName(repository.LoadedFilePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        string fileName = Path.GetFileName(repository.LoadedFilePath);
        return new FolderTarget($"График {graphNumber} — {fileName}", folderPath);
    }

    private async Task<FolderTarget?> SelectStatusFolderTargetAsync(
        IReadOnlyList<FolderTarget> targets)
    {
        RadioButtons choices = new()
        {
            Header = "Выберите файл данных:",
            SelectedIndex = 0
        };
        foreach (FolderTarget target in targets)
        {
            choices.Items.Add(target.Label);
        }

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Открыть папку",
            Content = choices,
            PrimaryButtonText = "Открыть",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            && choices.SelectedIndex >= 0
            && choices.SelectedIndex < targets.Count
                ? targets[choices.SelectedIndex]
                : null;
    }

    private async Task OpenStatusFolderAsync(FolderTarget target)
    {
        if (!Directory.Exists(target.FolderPath))
        {
            await ShowOperationMessageAsync("Папка", "Папка файла данных недоступна.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.FolderPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            await ShowOperationMessageAsync("Папка", "Не удалось открыть папку в проводнике.");
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
        List<float> segmentBoundaryXValues = BuildVisibleSegmentBoundaryXValues(
            repository,
            graphConfiguration.XAxisType,
            allXAxisData,
            visibleRange);
        List<ChartNoteMarker> noteMarkers = BuildVisibleNoteMarkers(
            graphNumber,
            graphConfiguration,
            allXAxisData,
            visibleRange);
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
            viewport.RightYRange,
            segmentBoundaryXValues,
            noteMarkers,
            visibleRange.StartIndex);
        graphControl.IsFlowMode = viewport.IsFlowEnabled;
        graphControl.ConfigureScrollBars(
            repository.DataSize,
            visibleRange.StartIndex,
            visibleRange.EndIndex,
            !viewport.IsFlowEnabled && viewport.HasCustomXRange);
        UpdateRangeControls(graphNumber);
    }

    private List<ChartNoteMarker> BuildVisibleNoteMarkers(
        int graphNumber,
        ChartConfiguration configuration,
        IReadOnlyList<float> allXAxisData,
        ChartIndexRange visibleRange)
    {
        bool showNotes = graphNumber == 1
            ? Chart1ShowNotesCheckBox.IsChecked == true
            : Chart2ShowNotesCheckBox.IsChecked == true;
        if (!showNotes
            || (graphNumber == 2 && chartWorkspace.IsGraph2Independent)
            || configuration.Curves.Count == 0)
        {
            return [];
        }

        EnsureNotesLoadedForCurrentSource();
        if (!notesModule.IsLoaded || notesModule.NotesPath.Length == 0)
        {
            return [];
        }

        ulong configKey = NotesModule.ComputeConfigKey(configuration);
        List<ChartNoteMarker> markers = [];
        foreach (ChartNote note in notesModule.GetNotesForConfig(configKey))
        {
            if (note.XIndex < 0
                || note.XIndex < visibleRange.StartIndex - 1
                || note.XIndex > visibleRange.EndIndex + 1
                || note.XIndex >= allXAxisData.Count)
            {
                continue;
            }

            markers.Add(new ChartNoteMarker(
                note.NoteId,
                allXAxisData[note.XIndex],
                note.YLeft,
                note.YRight,
                note.HasYRight,
                note.AnchorAxisGroup,
                note.Text));
        }

        return markers;
    }

    private bool EnsureNotesLoadedForCurrentSource()
    {
        string notesPath = ResolveCurrentNotesPath();
        bool changed = !string.Equals(notesModule.NotesPath, notesPath, StringComparison.Ordinal);
        if (changed)
        {
            notesModule.SetNotesPath(notesPath);
        }

        if (!notesModule.IsLoaded)
        {
            notesModule.LoadFromDisk();
        }

        return changed;
    }

    private string ResolveCurrentNotesPath()
    {
        if (currentDataFilePath.Length > 0)
        {
            return currentDataFilePath + ".notes.jsonl";
        }

        SerialStatusSnapshot status = serialDeviceService.GetStatus();
        if (!serialRecordingEnabled || !serialDeviceService.IsConnected)
        {
            return string.Empty;
        }

        string recordPath = status.RecordFilePath.Length > 0
            ? status.RecordFilePath
            : configuredAppendRecordPath;
        return recordPath.Length > 0 ? recordPath + ".notes.jsonl" : string.Empty;
    }

    private void ChartPointSelected(int graphNumber, ChartPointSelectedEventArgs args)
    {
        activeChartNumber = graphNumber;
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        if (graphNumber == 1)
        {
            chart1Crosshair = args;
            CursorXTextBox.Text = args.SourceIndex.ToString(CultureInfo.CurrentCulture);
            CursorY1TextBox.Text = FormatCursorValue(args.YLeft, configuration.LeftAxisUnits);
        }
        else
        {
            chart2Crosshair = args;
            Cursor2XTextBox.Text = args.SourceIndex.ToString(CultureInfo.CurrentCulture);
            Cursor2Y1TextBox.Text = FormatCursorValue(args.YLeft, configuration.LeftAxisUnits);
        }
    }

    private void Cursor1ClearButton_Click(object sender, RoutedEventArgs e) => ClearChartCrosshair(1);

    private void Cursor2ClearButton_Click(object sender, RoutedEventArgs e) => ClearChartCrosshair(2);

    private void ClearChartCrosshair(int graphNumber)
    {
        activeChartNumber = graphNumber;
        if (graphNumber == 1)
        {
            chart1Crosshair = null;
            CursorXTextBox.Text = "—";
            CursorY1TextBox.Text = "—";
            GraphControl.ClearCrosshair();
        }
        else
        {
            chart2Crosshair = null;
            Cursor2XTextBox.Text = "—";
            Cursor2Y1TextBox.Text = "—";
            Graph2Control.ClearCrosshair();
        }
    }

    private static string FormatCursorValue(double value, string units)
    {
        string formatted = units switch
        {
            "mdeg" => value.ToString("F0", CultureInfo.CurrentCulture),
            "mV" => value.ToString("F2", CultureInfo.CurrentCulture),
            "V/V" => value.ToString("F4", CultureInfo.CurrentCulture),
            "degC" or "°C" => value.ToString("F2", CultureInfo.CurrentCulture),
            _ => value.ToString("F2", CultureInfo.CurrentCulture)
        };
        string displayUnits = units is "degC" or "°C" ? "°C" : units;
        return displayUnits.Length == 0 ? formatted : $"{formatted} {displayUnits}";
    }

    private void ShowChartContextMenu(
        int graphNumber,
        LineChartControl graphControl,
        ChartContextRequestedEventArgs args)
    {
        activeChartNumber = graphNumber;
        MenuFlyout menu = new();
        MenuFlyoutItem setXFromItem = new()
        {
            Text = "Установить как X начальное",
            IsEnabled = args.HasCrosshair
        };
        setXFromItem.Click += (_, _) => SetXRangeBoundaryFromCrosshair(graphNumber, setStart: true);
        menu.Items.Add(setXFromItem);

        MenuFlyoutItem setXToItem = new()
        {
            Text = "Установить как X конечное",
            IsEnabled = args.HasCrosshair
        };
        setXToItem.Click += (_, _) => SetXRangeBoundaryFromCrosshair(graphNumber, setStart: false);
        menu.Items.Add(setXToItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem addNoteItem = new()
        {
            Text = "Создать заметку",
            IsEnabled = args.HasCrosshair
                && chartWorkspace.GetRepositoryForGraph(graphNumber).HasData
                && !(graphNumber == 2 && chartWorkspace.IsGraph2Independent)
        };
        addNoteItem.Click += async (_, _) => await CreateNoteAtCrosshairAsync(graphNumber);
        menu.Items.Add(addNoteItem);
        menu.ShowAt(graphControl, new FlyoutShowOptions { Position = args.Position });
    }

    private void SetXRangeBoundaryFromCrosshair(int graphNumber, bool setStart)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        ChartPointSelectedEventArgs? selected = graphNumber == 1 ? chart1Crosshair : chart2Crosshair;
        LineChartControl graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        if (!repository.HasData || selected is null || !graphControl.HasCrosshair)
        {
            return;
        }

        int selectedX = Math.Clamp(selected.SourceIndex, 0, Math.Max(0, repository.DataSize - 1));
        string value = selectedX.ToString(CultureInfo.CurrentCulture);
        if (graphNumber == 1)
        {
            if (setStart)
            {
                XRangeFromTextBox.Text = value;
            }
            else
            {
                XRangeToTextBox.Text = value;
            }

            Graph1ResetRangeButton.IsEnabled = true;
        }
        else
        {
            if (setStart)
            {
                XRange2FromTextBox.Text = value;
            }
            else
            {
                XRange2ToTextBox.Text = value;
            }

            Graph2ResetRangeButton.IsEnabled = true;
        }
    }

    private async Task CreateNoteAtCrosshairAsync(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        ChartPointSelectedEventArgs? selected = graphNumber == 1 ? chart1Crosshair : chart2Crosshair;
        LineChartControl graphControl = graphNumber == 1 ? GraphControl : Graph2Control;
        if (!repository.HasData)
        {
            await ShowNotesMessageAsync("Заметки", "Нет данных для создания заметки.");
            return;
        }

        if (selected is null || !graphControl.HasCrosshair)
        {
            await ShowNotesMessageAsync(
                "Заметки",
                "Сначала выберите точку на графике (клик по области графика).");
            return;
        }

        if (graphNumber == 2 && chartWorkspace.IsGraph2Independent)
        {
            return;
        }

        string notesPath = ResolveCurrentNotesPath();
        if (notesPath.Length == 0)
        {
            await ShowNotesMessageAsync(
                "Заметки",
                "Не найден файл данных для привязки заметок.\nОткройте файл данных или включите запись при работе через COM.");
            return;
        }

        TextBox editor = new()
        {
            MaxLength = 4095,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 190,
            MinWidth = 520
        };
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Создание заметки",
            Content = editor,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        EnsureNotesLoadedForCurrentSource();
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        int xIndex = Math.Clamp(selected.SourceIndex, 0, Math.Max(0, repository.DataSize - 1));
        ChartNote note = new()
        {
            NoteId = NotesModule.GenerateNoteId(),
            CreatedUtcMs = NotesModule.NowUtcMs(),
            ConfigKey = NotesModule.ComputeConfigKey(configuration),
            ConfigSnapshot = NotesModule.BuildConfigSnapshot(configuration),
            PresetName = configuration.PresetDisplayName,
            XAxisType = configuration.XAxisType,
            XIndex = xIndex,
            YLeft = selected.YLeft,
            YRight = selected.YRight,
            HasYRight = selected.HasRightAxis,
            AnchorAxisGroup = 0,
            Text = editor.Text
        };
        if (!notesModule.AppendNote(note))
        {
            await ShowNotesMessageAsync("Заметки", "Не удалось сохранить заметку на диск.");
            return;
        }

        RenderNotesOnLinkedGraphs();
    }

    private async Task OpenNoteAsync(int graphNumber, string noteId)
    {
        activeChartNumber = graphNumber;
        EnsureNotesLoadedForCurrentSource();
        ChartNote? note = notesModule.GetAllNotes().FirstOrDefault(item => item.NoteId == noteId);
        if (note is null)
        {
            return;
        }

        TextBox editor = new()
        {
            Text = note.Text,
            MaxLength = 4095,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 190,
            MinWidth = 520,
            SelectionLength = note.Text.Length
        };
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Заметка",
            Content = editor,
            PrimaryButtonText = "Сохранить",
            SecondaryButtonText = "Удалить",
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (!UpdateNoteTextById(note.NoteId, editor.Text))
            {
                await ShowNotesMessageAsync("Заметки", "Не удалось сохранить изменения заметки.");
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = "Удаление заметки",
                Content = "Удалить эту заметку?\nДействие нельзя отменить.",
                PrimaryButtonText = "Удалить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
            {
                if (!notesModule.DeleteNoteById(note.NoteId))
                {
                    await ShowNotesMessageAsync("Заметки", "Не удалось удалить заметку.");
                }
                else
                {
                    RenderNotesOnLinkedGraphs();
                }
            }
        }
    }

    private bool UpdateNoteTextById(string noteId, string newText)
    {
        if (ResolveCurrentNotesPath().Length == 0)
        {
            return false;
        }

        EnsureNotesLoadedForCurrentSource();
        if (!notesModule.UpdateNoteTextById(noteId, newText))
        {
            return false;
        }

        RenderNotesOnLinkedGraphs();
        return true;
    }

    private void RenderNotesOnLinkedGraphs()
    {
        RenderGraph(1);
        if (!chartWorkspace.IsGraph2Independent && DualModeCheckBox.IsChecked == true)
        {
            RenderGraph(2);
        }
    }

    private void ChartShowNotesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, Chart1ShowNotesCheckBox))
        {
            RenderGraph(1);
        }
        else if (ReferenceEquals(sender, Chart2ShowNotesCheckBox))
        {
            RenderGraph(2);
        }
    }

    private void NotesButton_Click(object sender, RoutedEventArgs e)
    {
        notesJournalWindow ??= new NotesJournalWindow(
            GetNotesJournalSource,
            GetActiveChartNumber,
            ApplyNoteConfiguration,
            UpdateNoteTextById);
        notesJournalWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    private NotesJournalSource GetNotesJournalSource()
    {
        string notesPath = ResolveCurrentNotesPath();
        if (notesPath.Length == 0)
        {
            return new NotesJournalSource(false, string.Empty, []);
        }

        EnsureNotesLoadedForCurrentSource();
        return new NotesJournalSource(true, notesPath, notesModule.GetAllNotes());
    }

    private int GetActiveChartNumber()
    {
        return DualModeCheckBox.IsChecked == true && activeChartNumber == 2 ? 2 : 1;
    }

    private NoteOperationResult ApplyNoteConfiguration(ChartNote note, int graphNumber)
    {
        if (graphNumber is not 1 and not 2)
        {
            return NoteOperationResult.Failed("Некорректный номер графика.");
        }

        if (!NotesModule.ParseConfigSnapshot(
                note.ConfigSnapshot,
                out XAxisType xAxisType,
                out PresetType preset,
                out List<SnapshotCurveItem> parsedCurves))
        {
            return NoteOperationResult.Failed("Не удалось прочитать конфигурацию кривых из заметки.");
        }

        FrequencyBand frequency = FrequencyBand.Freq400KHz;
        foreach (SnapshotCurveItem parsedCurve in parsedCurves)
        {
            if (parsedCurve.CurveId.Contains("2000", StringComparison.Ordinal))
            {
                frequency = FrequencyBand.Freq2000KHz;
                break;
            }

            if (parsedCurve.CurveId.Contains("400", StringComparison.Ordinal))
            {
                frequency = FrequencyBand.Freq400KHz;
                break;
            }
        }

        ChartConfiguration configuration;
        if (preset is not PresetType.None and not PresetType.Custom)
        {
            SchematicConfigurationEditor editor = new(
                curveSelectionService,
                SchematicSelection.UiTransmitterCount,
                new ChartConfiguration());
            editor.SetFrequency(frequency);
            editor.ApplyPreset(preset);
            configuration = editor.BuildConfiguration();
        }
        else
        {
            configuration = chartWorkspace.GetConfigurationForGraph(graphNumber).Copy();
        }

        if (configuration.Curves.Count == 0)
        {
            configuration = curveSelectionService.BuildFullCurveSet(frequency);
        }

        if (configuration.Curves.Count == 0)
        {
            return NoteOperationResult.Failed(
                "В текущем состоянии графика нельзя восстановить конфигурацию заметки.");
        }

        int enabledCount = EnableSnapshotCurves(configuration, parsedCurves);
        if (enabledCount <= 0)
        {
            configuration = curveSelectionService.BuildFullCurveSet(frequency);
            enabledCount = EnableSnapshotCurves(configuration, parsedCurves);
        }

        if (enabledCount <= 0)
        {
            return NoteOperationResult.Failed(
                "Не найдено ни одной совместимой кривой для применения заметки.");
        }

        configuration.XAxisType = xAxisType;
        configuration.ActivePreset = preset;
        if (note.PresetName.Length > 0)
        {
            configuration.PresetDisplayName = note.PresetName;
        }

        configuration.UpdateAxisConfiguration();
        if (!curveSelectionService.ValidateConfiguration(configuration))
        {
            return NoteOperationResult.Failed(
                "Конфигурация из заметки не прошла проверку валидности.");
        }

        chartWorkspace.ApplyConfigurationForGraph(graphNumber, configuration);
        schematicWindow?.RefreshDataContext(graphNumber);
        RenderGraph(graphNumber);
        return NoteOperationResult.Ok;
    }

    private static int EnableSnapshotCurves(
        ChartConfiguration configuration,
        IReadOnlyList<SnapshotCurveItem> parsedCurves)
    {
        foreach (CurveDefinition curve in configuration.Curves)
        {
            curve.Enabled = false;
        }

        int enabledCount = 0;
        foreach (SnapshotCurveItem parsedCurve in parsedCurves)
        {
            CurveDefinition? curve = configuration.Curves.FirstOrDefault(
                item => item.Id == parsedCurve.CurveId);
            if (curve is null)
            {
                continue;
            }

            curve.Enabled = true;
            curve.YAxisGroup = parsedCurve.YAxisGroup;
            enabledCount++;
        }

        return enabledCount;
    }

    private async Task ShowNotesMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "ОК"
        };
        await dialog.ShowAsync();
    }

    private static List<float> BuildVisibleSegmentBoundaryXValues(
        ToolDataRepository repository,
        XAxisType xAxisType,
        IReadOnlyList<float> allXAxisData,
        ChartIndexRange visibleRange)
    {
        List<float> values = [];
        if (string.IsNullOrWhiteSpace(repository.LoadedFilePath)
            || !ToolDataFileConstructor.TryLoadSegmentLayout(
                repository.LoadedFilePath,
                out IReadOnlyList<FileSegmentLayout> layout))
        {
            return values;
        }

        foreach (FileSegmentLayout segment in layout.Skip(1))
        {
            int index = segment.StartIndex;
            if (index <= 0
                || index >= allXAxisData.Count
                || index < visibleRange.StartIndex
                || index > visibleRange.EndIndex)
            {
                continue;
            }

            if (xAxisType == XAxisType.Temperature)
            {
                float before = allXAxisData[index - 1];
                float after = allXAxisData[index];
                if (float.IsFinite(before) && float.IsFinite(after))
                {
                    values.Add((before + after) / 2f);
                }
            }
            else
            {
                values.Add(index - 0.5f);
            }
        }

        return values;
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

        RenderGraph(graphNumber);
        SetStatus(enabled ? $"График {graphNumber}: Поток ON ({viewport.FlowWindowSize})" : $"График {graphNumber}: Поток OFF");
    }

    private void ApplyPointerZoom(int graphNumber, ChartZoomRequestedEventArgs args)
    {
        activeChartNumber = graphNumber;
        if (graphNumber == 1)
        {
            chart1Crosshair = null;
        }
        else
        {
            chart2Crosshair = null;
        }

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

        ContentDialogResult action = await ShowSaveRangeActionDialogAsync();
        if (action == ContentDialogResult.None)
        {
            return;
        }

        if (action == ContentDialogResult.Secondary)
        {
            Windows.Storage.StorageFile? target = await PickExistingCanonicalBinAsync(
                "Выберите существующий файл для добавления отрезка");
            if (target is null)
            {
                return;
            }

            try
            {
                FileConstructorSegment segment = ToolDataFileConstructor.CreateSegment(
                    repository.LoadedFilePath,
                    range.StartIndex,
                    repository.Records.Skip(range.StartIndex).Take(range.EndIndex - range.StartIndex + 1),
                    $"Сохранённый отрезок: {repository.LoadedFileName} [{range.StartIndex}..{range.EndIndex}]");
                FileConstructorSaveResult result = await Task.Run(
                    () => ToolDataFileConstructor.AppendSegmentsToExistingFile(target.Path, [segment]));

                if (PathsEqual(target.Path, repository.LoadedFilePath))
                {
                    ReloadGraph1AfterFileChange(repository);
                }

                string message = result.Warning
                    ?? $"Отрезок [{range.StartIndex}..{range.EndIndex}] добавлен к файлу:{Environment.NewLine}{target.Path}";
                SetStatus(result.LayoutSaved ? "Выбранный отрезок добавлен к существующему файлу" : "Отрезок добавлен с предупреждением");
                await ShowOperationMessageAsync(result.LayoutSaved ? "Готово" : "Предупреждение", message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                SetStatus("Не удалось добавить выбранный отрезок");
                await ShowOperationMessageAsync("Ошибка добавления отрезка", exception.Message);
            }

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
        if (saved)
        {
            ToolDataFileConstructor.DeleteSegmentLayout(file.Path);
        }

        SetStatus(saved ? $"Сохранён участок {range.StartIndex}–{range.EndIndex}" : "Не удалось сохранить выбранный участок");
    }

    private async Task<ContentDialogResult> ShowSaveRangeActionDialogAsync()
    {
        ContentDialogResult result = ContentDialogResult.None;
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Сохранение отрезка",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock description = new()
        {
            Text = "Выберите, что сделать с выбранным отрезком. Добавление возможно только в канонический .bin файл с такой же сигнатурой прибора.",
            TextWrapping = TextWrapping.Wrap
        };

        Button saveSeparateButton = new()
        {
            Content = "Сохранить отдельным",
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        saveSeparateButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];

        Button appendButton = new()
        {
            Content = "Добавить к существующему",
            MinWidth = 270,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Button cancelButton = new()
        {
            Content = "Отмена",
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid buttons = new()
        {
            ColumnSpacing = 12
        };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        Grid.SetColumn(appendButton, 1);
        Grid.SetColumn(cancelButton, 2);
        buttons.Children.Add(saveSeparateButton);
        buttons.Children.Add(appendButton);
        buttons.Children.Add(cancelButton);

        StackPanel content = new()
        {
            Width = 634,
            Spacing = 20
        };
        content.Children.Add(description);
        content.Children.Add(buttons);
        dialog.Content = content;

        saveSeparateButton.Click += (_, _) =>
        {
            result = ContentDialogResult.Primary;
            dialog.Hide();
        };
        appendButton.Click += (_, _) =>
        {
            result = ContentDialogResult.Secondary;
            dialog.Hide();
        };
        cancelButton.Click += (_, _) => dialog.Hide();

        await dialog.ShowAsync();
        return result;
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

        RadioButton currentFileOption = new()
        {
            Content = "Сохранить текущий файл с вырезанным отрезком",
            IsChecked = true,
            GroupName = "CutRangeAction"
        };
        RadioButton copyOption = new()
        {
            Content = "Сохранить копию текущего файла",
            GroupName = "CutRangeAction"
        };
        RadioButton appendOption = new()
        {
            Content = "Добавить файл с вырезанным отрезком к другому файлу",
            GroupName = "CutRangeAction"
        };
        StackPanel cutOptions = new() { Spacing = 8 };
        cutOptions.Children.Add(new TextBlock
        {
            Text = $"Выберите действие для записей {range.StartIndex}–{range.EndIndex}. Добавление к другому файлу не изменяет текущий файл.",
            TextWrapping = TextWrapping.Wrap
        });
        cutOptions.Children.Add(currentFileOption);
        cutOptions.Children.Add(copyOption);
        cutOptions.Children.Add(appendOption);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Вырезание выбранного участка",
            Content = cutOptions,
            PrimaryButtonText = "Продолжить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        if (appendOption.IsChecked == true)
        {
            await AppendFileWithoutRange(repository, range);
            return;
        }

        if (currentFileOption.IsChecked == true)
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
        if (saved)
        {
            bool layoutSaved = ToolDataFileConstructor.SaveLayoutExcludingRange(
                repository.LoadedFilePath,
                target.Path,
                range.StartIndex,
                range.EndIndex,
                repository.DataSize,
                out string? layoutError);
            if (!layoutSaved && !string.IsNullOrWhiteSpace(layoutError))
            {
                await ShowOperationMessageAsync("Границы сегментов", layoutError);
            }
        }

        SetStatus(saved ? "Копия без выбранного участка сохранена" : "Не удалось сохранить копию");
    }

    private async Task AppendFileWithoutRange(ToolDataRepository repository, ChartIndexRange range)
    {
        Windows.Storage.StorageFile? target = await PickExistingCanonicalBinAsync(
            "Выберите существующий файл для добавления данных");
        if (target is null)
        {
            return;
        }

        if (PathsEqual(target.Path, repository.LoadedFilePath))
        {
            await ShowOperationMessageAsync(
                "Вырезание отрезка",
                "Для этого действия выберите другой файл. Текущий файл не изменяется.");
            return;
        }

        try
        {
            List<FileConstructorSegment> keptSegments = [];
            if (range.StartIndex > 0)
            {
                keptSegments.Add(ToolDataFileConstructor.CreateSegment(
                    repository.LoadedFilePath,
                    0,
                    repository.Records.Take(range.StartIndex),
                    $"Файл с вырезанным отрезком: {repository.LoadedFileName} [0..{range.StartIndex - 1}]"));
            }

            if (range.EndIndex < repository.DataSize - 1)
            {
                keptSegments.Add(ToolDataFileConstructor.CreateSegment(
                    repository.LoadedFilePath,
                    range.EndIndex + 1,
                    repository.Records.Skip(range.EndIndex + 1),
                    $"Файл с вырезанным отрезком: {repository.LoadedFileName} [{range.EndIndex + 1}..{repository.DataSize - 1}]"));
            }

            FileConstructorSaveResult result = await Task.Run(
                () => ToolDataFileConstructor.AppendSegmentsToExistingFile(target.Path, keptSegments));
            string message = result.Warning
                ?? $"Файл без отрезка [{range.StartIndex}..{range.EndIndex}] добавлен к файлу:{Environment.NewLine}{target.Path}{Environment.NewLine}{Environment.NewLine}Текущий файл не изменён.";
            SetStatus(result.LayoutSaved ? "Файл с вырезанным отрезком добавлен" : "Файл добавлен с предупреждением");
            await ShowOperationMessageAsync(result.LayoutSaved ? "Готово" : "Предупреждение", message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            SetStatus("Не удалось добавить файл с вырезанным отрезком");
            await ShowOperationMessageAsync("Ошибка добавления файла", exception.Message);
        }
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
            bool layoutSaved = ToolDataFileConstructor.SaveLayoutExcludingRange(
                source,
                source,
                range.StartIndex,
                range.EndIndex,
                repository.DataSize + (range.EndIndex - range.StartIndex + 1),
                out string? layoutError);
            ReloadGraph1AfterFileChange(repository);
            SetStatus("Выбранный участок вырезан из текущего файла");
            if (!layoutSaved && !string.IsNullOrWhiteSpace(layoutError))
            {
                await ShowOperationMessageAsync("Границы сегментов", layoutError);
            }
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

    private async Task<Windows.Storage.StorageFile?> PickExistingCanonicalBinAsync(string title)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            CommitButtonText = title
        };
        picker.FileTypeFilter.Add(".bin");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFileAsync();
    }

    private void ReloadGraph1AfterFileChange(ToolDataRepository repository)
    {
        repository.LoadDataFromFile(repository.LoadedFilePath);
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

    private async Task ShowOperationMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "ОК"
        };
        await dialog.ShowAsync();
    }

    private static bool PathsEqual(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
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
        FolderStatusButton.Visibility = hasAnyData || connected ? Visibility.Visible : Visibility.Collapsed;
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
        fileConstructorWindow?.ClosePermanently();
        fileConstructorWindow = null;
        notesJournalWindow?.ClosePermanently();
        notesJournalWindow = null;
        serialDeviceService.Dispose();
        autoUpdateService.Dispose();
        autoUpdateCancellation.Dispose();
    }
}
