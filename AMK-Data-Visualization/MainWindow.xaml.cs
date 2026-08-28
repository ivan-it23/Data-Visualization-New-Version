using AMK.DataVisualization.App.Controls;
using AMK.DataVisualization.Charting.Notes;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.DeviceRegistry;
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
using System.Buffers.Binary;
using System.Globalization;
using System.Diagnostics;

namespace AMK.DataVisualization.App;

public sealed partial class MainWindow : Window
{
    private sealed record FolderTarget(string Label, string FolderPath);

    private const string DocumentationUrl =
        "http://ivanbohg.beget.tech/FilesForProgramVisualisation/Документация.pdf";

    // The control panels and both graphs need room, so the window may not be shrunk
    // below this share of the monitor it sits on.
    private const double MinimumWindowWidthFraction = 0.90;
    private const double MinimumWindowHeightFraction = 0.80;

    private readonly CurveSelectionService curveSelectionService = new();
    private readonly ChartWorkspace chartWorkspace = new(new ChartConfiguration(), new ChartConfiguration());
    private readonly SerialDeviceService serialDeviceService = new();
    private readonly SerialConnectionSettingsStore serialSettingsStore = new();
    private readonly MetrologySession metrologySession = new();
    private readonly AutoUpdateService autoUpdateService = new();
    private readonly NotesModule notesModule = new();
    private readonly CancellationTokenSource autoUpdateCancellation = new();
    private readonly DeviceRegistryConnection deviceRegistryConnection = new();
    private readonly DeviceRegistryUploadFlow deviceRegistryUploadFlow;
    private DeviceRegistryWindow? deviceRegistryWindow;
    private SchematicWindow? schematicWindow;
    private ConnectionSettingsWindow? connectionSettingsWindow;
    private MetrologyWindow? metrologyWindow;
    private FileConstructorWindow? fileConstructorWindow;
    private NotesJournalWindow? notesJournalWindow;
    private DeviceLogWindow? deviceLogWindow;
    private WindowScaleWindow? windowScaleWindow;
    private string currentDataFilePath = string.Empty;
    private string configuredAppendRecordPath = string.Empty;

    /// <summary>
    /// Файл, в который писала текущая сессия опроса.
    /// </summary>
    /// <remarks>
    /// Переживает отключение прибора: кадры остаются на графике, файл остаётся
    /// на диске, и заметки должны продолжать привязываться к нему. Сбрасывается
    /// началом новой сессии и открытием другого файла данных.
    /// </remarks>
    private string sessionRecordFilePath = string.Empty;
    private bool serialRecordingEnabled;
    private int activeChartNumber = 1;
    private ChartPointSelectedEventArgs? chart1Crosshair;
    private ChartPointSelectedEventArgs? chart2Crosshair;
    private bool initialMaximizeApplied;
    private bool updatingRangeControls;
    private bool startupUpdateCheckScheduled;
    private bool updateCheckInProgress;
    private readonly MinimumWindowSize minimumWindowSize =
        new(MinimumWindowWidthFraction, MinimumWindowHeightFraction);
    private readonly ChartDisplaySettingsStore chartDisplaySettingsStore = new();
    private readonly WindowScaleSettingsStore windowScaleSettingsStore = new();
    private readonly WindowScaleService windowScaleService = new();
    private ChartDisplaySettings chartDisplaySettings = new();
    private readonly Dictionary<int, DropDownButton> presetDropDowns = [];
    private readonly Dictionary<int, DropDownButton> frequencyDropDowns = [];
    private readonly Dictionary<int, InlineSchematicBar> inlineSchematicBars = [];
    private readonly Dictionary<int, SchematicConfigurationEditor?> inlineSchematicEditors = new() { [1] = null, [2] = null };
    private readonly Dictionary<int, ChartConfiguration?> inlineSchematicBaseConfig = new() { [1] = null, [2] = null };
    private readonly Dictionary<int, bool> inlineSchematicPending = new() { [1] = false, [2] = false };

    // Legacy keeps the "Zoom оси Y" choice per graph for the session only and
    // starts every session on "По двум осям".
    private readonly Dictionary<int, YZoomTarget> yZoomTargets = new()
    {
        [1] = YZoomTarget.Both,
        [2] = YZoomTarget.Both
    };

    public MainWindow()
    {
        deviceRegistryUploadFlow = new DeviceRegistryUploadFlow(deviceRegistryConnection);
        InitializeComponent();
        minimumWindowSize.Attach(WindowNative.GetWindowHandle(this));

        // Applied before anything reads the visual tree: the service re-parents the
        // window content under a host panel it sizes itself. The main window is
        // maximized, so only its content scales - the frame is left alone.
        windowScaleService.Initialize(windowScaleSettingsStore.Load());
        windowScaleService.ScaleChanged += WindowScaleService_ScaleChanged;
        windowScaleService.Attach(this, resizeWithScale: false, fitContentToWindow: true);
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
        GraphControl.HorizontalUndoRequested += (_, _) => UndoZoomAxis(1, isVertical: false);
        Graph2Control.HorizontalUndoRequested += (_, _) => UndoZoomAxis(2, isVertical: false);
        GraphControl.VerticalUndoRequested += (_, _) => UndoZoomAxis(1, isVertical: true);
        Graph2Control.VerticalUndoRequested += (_, _) => UndoZoomAxis(2, isVertical: true);
        GraphControl.PointSelected += (_, args) => ChartPointSelected(1, args);
        Graph2Control.PointSelected += (_, args) => ChartPointSelected(2, args);
        GraphControl.NoteMarkerActivated += async (_, args) => await OpenNoteAsync(1, args.NoteId);
        Graph2Control.NoteMarkerActivated += async (_, args) => await OpenNoteAsync(2, args.NoteId);
        GraphControl.ChartContextRequested += (_, args) => ShowChartContextMenu(1, GraphControl, args);
        Graph2Control.ChartContextRequested += (_, args) => ShowChartContextMenu(2, Graph2Control, args);
        GraphControl.DisplaySettingsRequested += (_, anchor) => ShowDisplaySettingsFlyout(1, anchor);
        Graph2Control.DisplaySettingsRequested += (_, anchor) => ShowDisplaySettingsFlyout(2, anchor);
        GraphControl.CurveVisibilityRequested += (_, anchor) => ShowCurveVisibilityFlyout(1, anchor);
        Graph2Control.CurveVisibilityRequested += (_, anchor) => ShowCurveVisibilityFlyout(2, anchor);
        chartDisplaySettings = chartDisplaySettingsStore.Load();
        ScaleFileMenuItems();
        InitializeDisplayOverlays();
        ShowLegacyEmptyState(1, hasData: false);
        ShowLegacyEmptyState(2, hasData: false);
        ApplyDisplayOverlayOptions(1);
        ApplyDisplayOverlayOptions(2);
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

        // Файл читается в собственное хранилище, и только удачное чтение
        // переключает на него график. Иначе неудачная попытка открыть файл
        // обрывала бы показ идущего опроса прибора.
        ToolDataRepository repository = chartWorkspace.Graph1FileRepository;
        try
        {
            repository.LoadDataFromFile(file.Path);
            if (fileConstructorWindow is not null)
            {
                await fileConstructorWindow.LoadOpenedDataFileAsync(file.Path);
            }

            LogLoadedDataFile(file.Path, repository);
            ShowGraph1File(file.Path);
            SetStatus("Готов");
            await ReportFileLoadIssuesAsync(repository);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            repository.ClearData();
            if (chartWorkspace.Graph1Source == Graph1DataSource.File)
            {
                schematicWindow?.RefreshDataContext(1, 2);
                GraphControl.ClearSeries("Ошибка загрузки");
                if (!chartWorkspace.IsGraph2Independent)
                {
                    Graph2Control.ClearSeries("Ошибка загрузки");
                }
            }

            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo();
            await ShowFileLoadErrorAsync(file.Path, ex);
        }
    }

    /// <summary>
    /// Переводит первый график на открытый файл данных. Опрос прибора при этом
    /// не прерывается: кадры продолжают приниматься и писаться в файл сессии, а
    /// кнопка подключения превращается в возврат к прибору.
    /// </summary>
    private void ShowGraph1File(string filePath)
    {
        currentDataFilePath = filePath;
        chartWorkspace.SetGraph1Source(Graph1DataSource.File);
        SwitchGraph1Source();
    }

    /// <summary>
    /// Возвращает первый график к кадрам прибора и отпускает открытый файл.
    /// </summary>
    private void ShowGraph1Device()
    {
        currentDataFilePath = string.Empty;
        chartWorkspace.Graph1FileRepository.ClearData();
        chartWorkspace.SetGraph1Source(Graph1DataSource.Device);
        SwitchGraph1Source();
    }

    /// <summary>
    /// Общая часть смены источника первого графика: заметки, состояние
    /// просмотра и перерисовка. Диапазон и история масштаба сбрасываются — у
    /// нового источника своя длина, и переносить на неё окно предыдущего
    /// нельзя.
    /// </summary>
    private void SwitchGraph1Source()
    {
        EnsureNotesLoadedForCurrentSource();

        // Перекрестие указывало на запись прежнего источника, и его координаты
        // к новому отношения не имеют.
        int previousActiveChart = activeChartNumber;
        ClearChartCrosshair(1);
        chartWorkspace.Graph1Viewport.ResetAll();
        if (!chartWorkspace.IsGraph2Independent)
        {
            ClearChartCrosshair(2);
            chartWorkspace.Graph2Viewport.ResetAll();
        }

        activeChartNumber = previousActiveChart;

        schematicWindow?.RefreshDataContext(1, 2);
        RenderGraph(1);
        if (!chartWorkspace.IsGraph2Independent)
        {
            RenderGraph(2);
        }

        // Обновляет и надпись на кнопке подключения, и строку состояния: она
        // называет источник, показанный на графике.
        UpdateConnectionControls(serialDeviceService.GetStatus());
    }

    /// <summary>
    /// Shows why a data file was rejected. The reason used to be discarded with the
    /// exception, so an unreadable file produced nothing but an empty chart and the
    /// user had no way to tell a wrong signature from a damaged file.
    /// </summary>
    private async Task ShowFileLoadErrorAsync(string filePath, Exception exception)
    {
        await ShowOperationMessageAsync(
            "Файл данных не открыт",
            $"{Path.GetFileName(filePath)}\n\n{exception.Message}");
    }

    /// <summary>
    /// Reports damaged and incomplete records the file was opened despite. Neither
    /// costs the user the rest of the file any more, so this is information rather
    /// than an error - but it has to be said out loud, otherwise a partly corrupt
    /// recording is indistinguishable from an intact one.
    /// </summary>
    private async Task ReportFileLoadIssuesAsync(ToolDataRepository repository)
    {
        int skippedFrames = repository.LoadedSkippedFrameCount;
        int trailingBytes = repository.LoadedTrailingByteCount;
        if (skippedFrames <= 0 && trailingBytes <= 0)
        {
            return;
        }

        List<string> lines = [];
        if (skippedFrames > 0)
        {
            lines.Add($"Повреждённых кадров пропущено: {skippedFrames}.");
        }

        if (trailingBytes > 0)
        {
            lines.Add($"Последняя запись оборвана — {trailingBytes} байт вместо полного кадра, пропущена.");
        }

        lines.Add($"Загружено кадров: {repository.DataSize}.");
        await ShowOperationMessageAsync("Файл открыт с замечаниями", string.Join("\n", lines));
    }

    private async void FileConstructorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        bool windowCreated = fileConstructorWindow is null;
        fileConstructorWindow ??= new FileConstructorWindow(OnFileConstructorSaved);
        windowScaleService.Attach(fileConstructorWindow, resizeWithScale: true);
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
            ToolDataRepository repository = chartWorkspace.Graph1FileRepository;
            repository.LoadDataFromFile(filePath);
            LogLoadedDataFile(filePath, repository);
            ShowGraph1File(filePath);
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
            metrologyWindow ??= new MetrologyWindow(
                serialDeviceService,
                metrologySession,
                deviceRegistryUploadFlow,
                OnMetrologyUpdated);
            windowScaleService.Attach(
                metrologyWindow,
                resizeWithScale: true,
                baseSizeTracksSystemDpi: true);
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
        bool closingForUpdate = false;
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

            // Установщик обновляет ту копию, которая установлена, а не ту, из
            // которой запущен процесс. Если это разные каталоги, пользователь
            // должен узнать об этом до загрузки: иначе он получит вторую копию
            // приложения и то же предложение обновиться при следующем запуске.
            string? copyMismatch = AutoUpdateService.DescribeCopyMismatch();
            string promptText =
                $"Доступна версия {remoteVersion}.\nТекущая версия: {currentVersion}.";
            promptText += copyMismatch is null
                ? "\n\nСкачать и установить обновление?"
                : $"\n\n{copyMismatch}\n\nВсё равно скачать и установить обновление?";

            ContentDialog prompt = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = "Обновление доступно",
                Content = new TextBlock
                {
                    Text = promptText,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Скачать и установить",
                CloseButtonText = "Позже",
                DefaultButton = copyMismatch is null
                    ? ContentDialogButton.Primary
                    : ContentDialogButton.Close
            };
            if (await prompt.ShowScaledAsync() != ContentDialogResult.Primary)
            {
                SetStatus("Обновление отложено");
                return;
            }

            SetStatus($"Загрузка обновления {remoteVersion}...");
            string installerPath = await autoUpdateService.DownloadInstallerAsync(update, cancellationToken);
            if (!AutoUpdateService.StartInstaller(installerPath, out string startError))
            {
                await ShowUpdateMessageAsync("Ошибка обновления", startError);
                SetStatus("Не удалось запустить установщик обновления");
                return;
            }

            SetStatus("Установщик обновления запущен");
            closingForUpdate = true;
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

            // Окно уже закрывается под установщик: его элементы к этому моменту
            // разрушены, и обращение к ним падает исключением, которое некому
            // обработать.
            if (!closingForUpdate)
            {
                UpdateCheckButton.IsEnabled = true;
            }
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
        await dialog.ShowScaledAsync();
    }

    private void ShowConnectionSettingsWindow()
    {
        connectionSettingsWindow ??= new ConnectionSettingsWindow(
            serialDeviceService,
            serialSettingsStore,
            PrepareSerialSession);
        windowScaleService.Attach(connectionSettingsWindow, resizeWithScale: true);
        connectionSettingsWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    private async void ConnectionToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (serialDeviceService.IsConnectionRequested)
        {
            // Пока показан открытый файл, эта кнопка возвращает к прибору, а не
            // отключает его: опрос идёт, и первым действием пользователь ждёт
            // возврата к живым кадрам, а не обрыва сессии. Отключение остаётся
            // на этой же кнопке следующим нажатием.
            if (chartWorkspace.Graph1Source == Graph1DataSource.File)
            {
                ShowGraph1Device();
                SetStatus("Показаны данные прибора");
                return;
            }

            await serialDeviceService.DisconnectAsync();
            SetStatus("Прибор отключён");
            return;
        }

        SerialConnectionSettings settings = serialSettingsStore.Load();
        IReadOnlyList<string> ports = await Task.Run(SerialDeviceService.GetAvailablePorts);
        if (string.IsNullOrWhiteSpace(settings.LastPort))
        {
            SetStatus("Выберите COM-порт в настройках подключения");
            ShowConnectionSettingsWindow();
            return;
        }

        if (!ports.Contains(settings.LastPort, StringComparer.OrdinalIgnoreCase))
        {
            // The saved port is not present right now. Instead of silently
            // switching to another port, tell the user why so they can connect
            // the device to this port and retry.
            SetStatus($"COM-порт {settings.LastPort} недоступен");
            await ShowUpdateMessageAsync(
                "Подключение к прибору",
                $"Сохранённый COM-порт {settings.LastPort} сейчас недоступен.\n\n" +
                "Подключите прибор к этому порту и нажмите «Включить» ещё раз, " +
                "либо выберите другой порт в настройках подключения.",
                compactCentered: true);
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
                await serialDeviceService.StartPollingAsync(serialDeviceService.PollIntervalMilliseconds);
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
            DeviceExchangeLog.Append("⏸ Опрос приостановлен");
            SetStatus("Опрос прибора приостановлен");
        }
        else
        {
            await serialDeviceService.StartPollingAsync(serialDeviceService.PollIntervalMilliseconds);
            DeviceExchangeLog.Append("▶ Опрос возобновлён");
            SetStatus("Опрос прибора продолжен");
        }

        UpdateConnectionControls(serialDeviceService.GetStatus());
    }

    private void PrepareSerialSession(SerialConnectionSettings settings)
    {
        currentDataFilePath = string.Empty;
        sessionRecordFilePath = string.Empty;
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

        // Новая сессия всегда начинается с показа прибора: файл, открытый до
        // неё, к этой сессии отношения не имеет.
        chartWorkspace.Graph1FileRepository.ClearData();
        chartWorkspace.SetGraph1Source(Graph1DataSource.Device);
        if (loadExisting)
        {
            // Дозапись идёт в этот же файл, поэтому его содержимое — начало
            // живого хранилища, а не отдельный источник.
            chartWorkspace.Graph1DeviceRepository.LoadDataFromFile(settings.ExistingRecordFile);
        }
        else
        {
            chartWorkspace.Graph1DeviceRepository.ClearData();
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
            // Кадр всегда идёт в живое хранилище, а не в то, что показано.
            // Пока открыт файл данных, опрос продолжается и пишется на диск, но
            // к содержимому файла принятые кадры отношения не имеют.
            ToolDataRepository deviceRepository = chartWorkspace.Graph1DeviceRepository;
            bool firstFrame = !deviceRepository.HasData;
            deviceRepository.AddParsedData(e.Frame);
            if (chartWorkspace.Graph1Source == Graph1DataSource.Device)
            {
                if (firstFrame)
                {
                    schematicWindow?.RefreshDataContext(1, 2);
                }

                RenderGraph(1);
                if (!chartWorkspace.IsGraph2Independent && DualModeCheckBox.IsChecked == true)
                {
                    RenderGraph(2);
                }
            }

            SetStatus($"Получен кадр {e.SessionFrameNumber}");
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

        // Сессия опроса идёт, но график показывает открытый файл: пользователю
        // нужен возврат к прибору, а не отключение. Отключение доступно после
        // возврата, этой же кнопкой.
        bool viewingFileWhileConnected = status.ConnectionRequested
            && chartWorkspace.Graph1Source == Graph1DataSource.File;
        ConnectionToggleButton.Content = status.ConnectionRequested
            ? viewingFileWhileConnected ? "К прибору →" : "Отключить"
            : "Включить";
        ToolTipService.SetToolTip(
            ConnectionToggleButton,
            viewingFileWhileConnected
                ? "Вернуться к данным прибора (опрос продолжается)"
                : "Включение/Отключение прибора");
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
            await ReportFileLoadIssuesAsync(chartWorkspace.Graph2IndependentRepository);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            schematicWindow?.RefreshDataContext(2);
            Graph2Control.ClearSeries("Ошибка загрузки");
            SetStatus("Ошибка загрузки");
            UpdateStatusBarFileInfo();
            await ShowFileLoadErrorAsync(file.Path, ex);
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
        List<FolderTarget> targets = new(3);
        FolderTarget? graph1Target = serialDeviceService.IsConnected
            ? CreateSerialRecordingFolderTarget()
            : CreateRepositoryFolderTarget(1, chartWorkspace.Graph1Repository);
        if (graph1Target is not null)
        {
            targets.Add(graph1Target);
        }

        // Прибор подключён, но на графике открытый файл: его каталог — тоже
        // рабочий, и предлагать один каталог записи было бы неверно.
        if (serialDeviceService.IsConnected && chartWorkspace.Graph1Source == Graph1DataSource.File)
        {
            FolderTarget? openedFileTarget = CreateRepositoryFolderTarget(
                1,
                chartWorkspace.Graph1FileRepository);
            if (openedFileTarget is not null)
            {
                targets.Add(openedFileTarget);
            }
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
        ContentDialogResult result = await dialog.ShowScaledAsync();
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
            windowScaleService.Attach(schematicWindow, resizeWithScale: true);
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

    // ----- On-chart curve-selection overlays (gear / preset dropdown / schematic / eye) -----

    private void InitializeDisplayOverlays()
    {
        BuildDisplayOverlay(1, GraphControl);
        BuildDisplayOverlay(2, Graph2Control);
    }

    private void BuildDisplayOverlay(int graphNumber, LineChartControl control)
    {
        DropDownButton presetButton = new()
        {
            Content = "Быстрые пресеты",
            MinWidth = 0,
            MinHeight = 0,
            Height = 28,
            Padding = new Thickness(10, 0, 8, 0),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            Flyout = BuildPresetFlyout(graphNumber)
        };
        presetDropDowns[graphNumber] = presetButton;
        control.LeftOverlayHost.Children.Add(presetButton);

        DropDownButton frequencyButton = new()
        {
            Content = "400 кГц",
            MinWidth = 0,
            MinHeight = 0,
            Height = 28,
            Padding = new Thickness(10, 0, 8, 0),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            Flyout = BuildFrequencyFlyout(graphNumber)
        };
        frequencyDropDowns[graphNumber] = frequencyButton;
        control.LeftOverlayHost.Children.Add(frequencyButton);

        InlineSchematicBar bar = new() { Visibility = Visibility.Collapsed };
        bar.ElementToggled += (_, toggle) => OnInlineSchematicElementToggled(graphNumber, toggle);
        bar.ApplyRequested += (_, _) => OnInlineSchematicApplyRequested(graphNumber);
        inlineSchematicBars[graphNumber] = bar;
        control.CenterOverlayHost.Children.Add(bar);
    }

    private MenuFlyout BuildFrequencyFlyout(int graphNumber)
    {
        MenuFlyout flyout = new();
        MenuFlyoutItem item400 = new() { Text = "400 кГц" };
        item400.Click += (_, _) => OnFrequencyChosen(graphNumber, FrequencyBand.Freq400KHz);
        MenuFlyoutItem item2000 = new() { Text = "2000 кГц" };
        item2000.Click += (_, _) => OnFrequencyChosen(graphNumber, FrequencyBand.Freq2000KHz);
        ScaleMenuItem(item400);
        flyout.Items.Add(item400);
        ScaleMenuItem(item2000);
        flyout.Items.Add(item2000);
        return flyout;
    }

    private void OnFrequencyChosen(int graphNumber, FrequencyBand frequency)
    {
        EnsureInlineEditor(graphNumber);
        SchematicConfigurationEditor editor = inlineSchematicEditors[graphNumber]!;
        editor.SetFrequency(frequency);
        RefreshOverlayLabels(graphNumber);

        // Re-apply the current selection at the new frequency when something is
        // actually shown; otherwise the frequency is simply remembered for the
        // next selection.
        if (editor.CanApply && chartWorkspace.GetRepositoryForGraph(graphNumber).HasData)
        {
            ApplyInlineEditorConfiguration(graphNumber);
        }
    }

    private void RefreshOverlayLabels(int graphNumber)
    {
        EnsureInlineEditor(graphNumber);
        SchematicConfigurationEditor editor = inlineSchematicEditors[graphNumber]!;
        if (presetDropDowns.TryGetValue(graphNumber, out DropDownButton? presetButton))
        {
            presetButton.Content = editor.CurrentPreset != PresetType.None && editor.CurrentPresetDisplayName.Length > 0
                ? editor.CurrentPresetDisplayName
                : "Быстрые пресеты";
        }

        if (frequencyDropDowns.TryGetValue(graphNumber, out DropDownButton? frequencyButton))
        {
            frequencyButton.Content = editor.Selection.Frequency == FrequencyBand.Freq400KHz ? "400 кГц" : "2000 кГц";
        }
    }

    private MenuFlyout BuildPresetFlyout(int graphNumber)
    {
        // Пресеты разделены по природе измеряемой величины: фаза и амплитуда.
        // УЭС попадает в фазовый раздел, потому что прибор считает его из
        // разности фаз (rho_ph_smt), а «УЭС по затуханию» — в амплитудный: там
        // обратная задача решается по затуханию.
        MenuFlyout flyout = new();
        AddPresetGroupHeader(flyout, "Фазовые измерения");
        AddPresetMenuItem(flyout, graphNumber, "Разность фаз", PresetType.PhaseDifferences, null);
        AddPresetMenuItem(flyout, graphNumber, "Разность фаз молчащих", PresetType.SilentPhaseDifference, null);
        AddPresetMenuItem(flyout, graphNumber, "Симм. фазы (на приборе)", PresetType.PhaseSmt, SmtComputationMode.FromSonde);
        AddPresetMenuItem(flyout, graphNumber, "Симм. фазы (на компьютере)", PresetType.PhaseSmt, SmtComputationMode.FromComputer);
        AddPresetMenuItem(flyout, graphNumber, "УЭС (на приборе)", PresetType.RhoSmt, SmtComputationMode.FromSonde);
        AddPresetMenuItem(flyout, graphNumber, "УЭС (на компьютере)", PresetType.RhoSmt, SmtComputationMode.FromComputer);

        // Затухание и амплитудное УЭС есть только у приборов новой прошивки.
        // Пункты показываются всегда, чтобы состав меню не менялся под руками,
        // но гасятся, когда источник их не отдаёт.
        AddPresetGroupHeader(flyout, "Амплитудные измерения");
        AddPresetMenuItem(flyout, graphNumber, "Амплитуды R1", PresetType.AllAmplitudesR1, null);
        AddPresetMenuItem(flyout, graphNumber, "Амплитуды R2", PresetType.AllAmplitudesR2, null);
        AddPresetMenuItem(flyout, graphNumber, "Амплитуды молчащих", PresetType.SilentAmplitudes, null);
        AddPresetMenuItem(flyout, graphNumber, "Отношение амплитуд", PresetType.AmplitudeRatios, null);
        AddPresetMenuItem(flyout, graphNumber, "Затухание (на приборе)", PresetType.AttDbSmt, SmtComputationMode.FromSonde);
        AddPresetMenuItem(flyout, graphNumber, "Затухание (на компьютере)", PresetType.AttDbSmt, SmtComputationMode.FromComputer);
        AddPresetMenuItem(flyout, graphNumber, "УЭС по затуханию (на приборе)", PresetType.RhoAttSmt, SmtComputationMode.FromSonde);
        AddPresetMenuItem(flyout, graphNumber, "УЭС по затуханию (на компьютере)", PresetType.RhoAttSmt, SmtComputationMode.FromComputer);

        // Температура не относится ни к фазовым, ни к амплитудным измерениям,
        // поэтому стоит отдельно и в разделы не входит.
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddPresetMenuItem(flyout, graphNumber, "Температура", PresetType.Temperature, null);

        flyout.Opening += (_, _) =>
        {
            bool extendedFirmware = chartWorkspace.IsExtendedFirmwareSourceForGraph(graphNumber);
            foreach (MenuFlyoutItemBase item in flyout.Items)
            {
                if (item is not MenuFlyoutItem menuItem || menuItem.Tag is not PresetMenuTag tag)
                {
                    continue;
                }

                bool enabled = tag.Mode != SmtComputationMode.FromComputer || metrologySession.HasData;
                if (tag.Preset is PresetType.RhoAttSmt or PresetType.AttDbSmt)
                {
                    enabled = enabled && extendedFirmware;
                }

                menuItem.IsEnabled = enabled;
            }
        };
        return flyout;
    }

    /// <summary>
    /// Добавляет заголовок раздела в меню пресетов.
    /// </summary>
    /// <remarks>
    /// У <c>MenuFlyout</c> нет собственного типа заголовка, а список
    /// <c>Items</c> принимает только наследников <c>MenuFlyoutItemBase</c>.
    /// Поэтому заголовок — это пункт без обработчика и без тега, отключённый
    /// навсегда: он не нажимается, не получает фокус с клавиатуры и не
    /// затрагивается общим обходом в <c>Opening</c>, который включает и гасит
    /// пункты по их тегу.
    ///
    /// Перед всеми разделами, кроме первого, ставится разделитель.
    /// </remarks>
    private void AddPresetGroupHeader(MenuFlyout flyout, string title)
    {
        if (flyout.Items.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem header = new()
        {
            Text = title,
            IsEnabled = false,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        ScaleMenuItem(header, MenuHeaderFontSize);
        flyout.Items.Add(header);
    }

    private void AddPresetMenuItem(MenuFlyout flyout, int graphNumber, string label, PresetType preset, SmtComputationMode? mode)
    {
        MenuFlyoutItem item = new() { Text = label, Tag = new PresetMenuTag(preset, mode) };
        ScaleMenuItem(item);
        item.Click += (_, _) => OnPresetChosen(graphNumber, preset, mode);
        flyout.Items.Add(item);
    }

    /// <summary>Кегль заголовка раздела в меню пресетов до масштабирования.</summary>
    private const double MenuHeaderFontSize = 12.0;

    /// <summary>Кегль пункта меню до масштабирования — базовый кегль темы.</summary>
    private const double MenuItemFontSize = 14.0;

    /// <summary>Высота пункта меню до масштабирования.</summary>
    private const double MenuItemHeight = 32.0;

    /// <summary>Боковой отступ пункта меню до масштабирования.</summary>
    private const double MenuItemSidePadding = 11.0;

    /// <summary>
    /// Задаёт пункту меню размер, соответствующий масштабу интерфейса.
    /// </summary>
    /// <remarks>
    /// <c>MenuFlyout</c> разворачивается в собственном окне, куда преобразование
    /// содержимого главного окна не попадает, поэтому меню оставалось в
    /// системном размере и на дисплее со 100 % выглядело мельче остального
    /// интерфейса. Масштабируется именно разметка, а не отрисовка: окно popup'а
    /// строится по разметке, поэтому размер верен с первого кадра, ничего не
    /// обрезается и не меняется в ответ на посторонний ввод.
    ///
    /// Список <c>ComboBox</c> сюда не входит: он разворачивается внутри окна и
    /// масштаб уже наследует.
    /// </remarks>
    private void ScaleMenuItem(MenuFlyoutItem item, double baseFontSize = MenuItemFontSize)
    {
        double scale = WindowScaleService.PopupContentScale;
        item.FontSize = baseFontSize * scale;
        item.Height = MenuItemHeight * scale;
        item.Padding = new Thickness(MenuItemSidePadding * scale, 0, MenuItemSidePadding * scale, 0);
    }

    private async void OnPresetChosen(int graphNumber, PresetType preset, SmtComputationMode? mode)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        if (!repository.HasData)
        {
            await ShowUpdateMessageAsync(
                "Нет данных для отображения",
                "Невозможно вывести кривые: данные отсутствуют.\nОткройте файл данных или подключите прибор.",
                compactCentered: true);
            return;
        }

        EnsureInlineEditor(graphNumber);
        SchematicConfigurationEditor editor = inlineSchematicEditors[graphNumber]!;
        if (mode is SmtComputationMode requestedMode
            && preset is PresetType.RhoSmt or PresetType.PhaseSmt or PresetType.RhoAttSmt or PresetType.AttDbSmt)
        {
            bool accepted = editor.TrySetSmtMode(preset, requestedMode, metrologySession.HasData, out string warning);
            if (!accepted && warning.Length > 0)
            {
                await ShowUpdateMessageAsync("Метрология недоступна", warning, compactCentered: true);
            }
        }
        else
        {
            editor.ApplyPreset(preset);
        }

        ApplyInlineEditorConfiguration(graphNumber);
    }

    private void OnInlineSchematicElementToggled(int graphNumber, SchematicElementToggle toggle)
    {
        EnsureInlineEditor(graphNumber);
        SchematicConfigurationEditor editor = inlineSchematicEditors[graphNumber]!;
        editor.ToggleElement(toggle.Type, toggle.Index);
        inlineSchematicPending[graphNumber] = true;
        UpdateInlineSchematicVisual(graphNumber);
    }

    private async void OnInlineSchematicApplyRequested(int graphNumber)
    {
        if (!inlineSchematicPending[graphNumber])
        {
            return;
        }

        SchematicConfigurationEditor? editor = inlineSchematicEditors[graphNumber];
        if (editor is null || !editor.CanApply)
        {
            return;
        }

        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        if (!repository.HasData)
        {
            await ShowUpdateMessageAsync(
                "Нет данных для отображения",
                "Невозможно вывести кривые: данные отсутствуют.\nОткройте файл данных или подключите прибор.",
                compactCentered: true);
            return;
        }

        ApplyInlineEditorConfiguration(graphNumber);
    }

    private void ApplyInlineEditorConfiguration(int graphNumber)
    {
        SchematicConfigurationEditor? editor = inlineSchematicEditors[graphNumber];
        if (editor is null || !editor.CanApply)
        {
            return;
        }

        ChartConfiguration configuration = editor.BuildConfiguration();
        if (!curveSelectionService.ValidateConfiguration(configuration))
        {
            return;
        }

        chartWorkspace.ApplyConfigurationForGraph(
            graphNumber,
            configuration,
            ChartConfigurationSource.OnChartOverlay);
        // Keep the existing inline editor as the source of truth for the on-chart
        // schematic: pointing the base config at the just-applied configuration
        // prevents EnsureInlineEditor from rebuilding it (a rebuild re-derives the
        // selection from curves and mis-highlights elements).
        inlineSchematicBaseConfig[graphNumber] = chartWorkspace.GetConfigurationForGraph(graphNumber);
        inlineSchematicPending[graphNumber] = false;
        schematicWindow?.RefreshDataContext(graphNumber);
        RefreshOverlayLabels(graphNumber);
        RenderGraph(graphNumber);
    }

    private void EnsureInlineEditor(int graphNumber)
    {
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        int transmitterCount = chartWorkspace.GetAvailableTransmitterCountForGraph(graphNumber);
        SchematicConfigurationEditor? existingEditor = inlineSchematicEditors[graphNumber];
        // The transmitter count belongs to the cache key: the editor is normally
        // created before any data is loaded, so without it a five-transmitter
        // selection would stay alive after a Картограф LWD 4tx source appears.
        // Capabilities are stable while data streams in, so live frames still do
        // not rebuild the editor and unsaved inline edits survive.
        if (existingEditor is not null
            && ReferenceEquals(inlineSchematicBaseConfig[graphNumber], configuration)
            && existingEditor.Selection.AvailableTransmitterCount == transmitterCount)
        {
            return;
        }

        // Same rule as the curve-setup window: a configuration built on the other
        // surface is not re-derived into a schematic selection, because a combined
        // selection cannot be recovered from curves without lighting up elements
        // the user never picked.
        inlineSchematicEditors[graphNumber] = new SchematicConfigurationEditor(
            curveSelectionService,
            transmitterCount,
            configuration,
            chartWorkspace.GetConfigurationSourceForGraph(graphNumber) == ChartConfigurationSource.OnChartOverlay);
        inlineSchematicBaseConfig[graphNumber] = configuration;
        inlineSchematicPending[graphNumber] = false;
    }

    private void UpdateInlineSchematicVisual(int graphNumber)
    {
        if (!inlineSchematicBars.TryGetValue(graphNumber, out InlineSchematicBar? bar))
        {
            return;
        }

        EnsureInlineEditor(graphNumber);
        SchematicConfigurationEditor editor = inlineSchematicEditors[graphNumber]!;
        // Quick presets and combined schematic selections are independent
        // branches: while a quick preset is active, no schematic element is
        // highlighted.
        bool showSelection = editor.CurrentPreset == PresetType.None;
        bar.UpdateState(editor.Selection, inlineSchematicPending[graphNumber], editor.CanApply, showSelection);
    }

    private void ApplyDisplayOverlayOptions(int graphNumber)
    {
        GraphDisplayOptions options = chartDisplaySettings.ForGraph(graphNumber);
        LineChartControl control = graphNumber == 1 ? GraphControl : Graph2Control;

        presetDropDowns[graphNumber].Visibility = options.ShowPresetDropdown ? Visibility.Visible : Visibility.Collapsed;
        frequencyDropDowns[graphNumber].Visibility = options.ShowFrequencyDropdown ? Visibility.Visible : Visibility.Collapsed;
        inlineSchematicBars[graphNumber].Visibility = options.ShowTopSchematic ? Visibility.Visible : Visibility.Collapsed;

        control.ShowCurveVisibilityToggle = options.ShowEye;
        // The overlays share a single line, so a fixed reserve clears the tallest
        // (the schematic) regardless of how many are shown.
        control.LegendTopReserve = options.AnyTopRow ? 42 : 0;

        RefreshOverlayLabels(graphNumber);
        if (options.ShowTopSchematic)
        {
            UpdateInlineSchematicVisual(graphNumber);
        }

        UpdateXAxisSelectorControls(graphNumber);
        RenderGraph(graphNumber);
    }

    // ----- X-axis selector in the graph control panel -----

    private void Graph1XAxisComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ChangeXAxisType(1, Graph1XAxisComboBox.SelectedIndex);

    private void Graph2XAxisComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ChangeXAxisType(2, Graph2XAxisComboBox.SelectedIndex);

    private void ChangeXAxisType(int graphNumber, int selectedIndex)
    {
        if (updatingRangeControls)
        {
            return;
        }

        XAxisType axisType = selectedIndex == 1 ? XAxisType.Temperature : XAxisType.FrameNumber;
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        if (configuration.XAxisType == axisType)
        {
            return;
        }

        configuration.XAxisType = axisType;
        // Both curve-setup surfaces must follow, otherwise the next apply from
        // either of them would silently restore the previous axis.
        if (inlineSchematicEditors[graphNumber] is { } inlineEditor)
        {
            inlineEditor.XAxisType = axisType;
        }

        schematicWindow?.RefreshDataContext(graphNumber);
        RenderGraph(graphNumber);
        SetStatus($"График {graphNumber}: ось X — {(axisType == XAxisType.Temperature ? "температура" : "номер кадра")}");
    }

    /// <summary>
    /// Shows the axis selector next to the device toggle for Graph 1 and at the end
    /// of the Graph 2 panel, and keeps it on the graph's current axis. Callers must
    /// already hold <see cref="updatingRangeControls"/>.
    /// </summary>
    private void UpdateXAxisSelectorControls(int graphNumber)
    {
        bool show = chartDisplaySettings.ForGraph(graphNumber).ShowXAxisDropdown;
        Visibility visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TextBlock label = graphNumber == 1 ? Graph1XAxisLabel : Graph2XAxisLabel;
        ComboBox selector = graphNumber == 1 ? Graph1XAxisComboBox : Graph2XAxisComboBox;
        label.Visibility = visibility;
        selector.Visibility = visibility;
        selector.SelectedIndex =
            chartWorkspace.GetConfigurationForGraph(graphNumber).XAxisType == XAxisType.Temperature ? 1 : 0;
    }

    private void ShowDisplaySettingsFlyout(int graphNumber, FrameworkElement anchor)
    {
        GraphDisplayOptions options = chartDisplaySettings.ForGraph(graphNumber);

        CheckBox presetCheck = CreateDisplayOptionCheckBox(
            "Список быстрых пресетов на графике",
            options.ShowPresetDropdown,
            enabled => UpdateDisplayOption(graphNumber, opt => opt.ShowPresetDropdown = enabled));
        CheckBox frequencyCheck = CreateDisplayOptionCheckBox(
            "Выбор частоты в левом углу графика",
            options.ShowFrequencyDropdown,
            enabled => UpdateDisplayOption(graphNumber, opt => opt.ShowFrequencyDropdown = enabled));
        CheckBox schematicCheck = CreateDisplayOptionCheckBox(
            "Схема прибора сверху графика",
            options.ShowTopSchematic,
            enabled => UpdateDisplayOption(graphNumber, opt => opt.ShowTopSchematic = enabled));
        CheckBox xAxisCheck = CreateDisplayOptionCheckBox(
            "Выбор оси X в панели управления графика",
            options.ShowXAxisDropdown,
            enabled => UpdateDisplayOption(graphNumber, opt => opt.ShowXAxisDropdown = enabled));

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"Отображение выбора кривых — График {graphNumber}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(presetCheck);
        content.Children.Add(frequencyCheck);
        content.Children.Add(schematicCheck);
        content.Children.Add(xAxisCheck);

        Flyout flyout = new() { Content = content };

        // Флайаут разворачивается в popup-слое окна, куда преобразование
        // содержимого не достаёт, поэтому масштаб задаётся его разметке —
        // так же, как пунктам меню.
        PopupScale.Apply(flyout);
        flyout.ShowAt(anchor);
    }

    private static CheckBox CreateDisplayOptionCheckBox(string label, bool isChecked, Action<bool> onChanged)
    {
        // A non-wrapping label keeps every option on a single line so the flyout
        // widens to fit the longest text instead of clipping it.
        CheckBox checkBox = new()
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.NoWrap },
            IsChecked = isChecked,
            MinWidth = 0
        };
        checkBox.Checked += (_, _) => onChanged(true);
        checkBox.Unchecked += (_, _) => onChanged(false);
        return checkBox;
    }

    private void UpdateDisplayOption(int graphNumber, Action<GraphDisplayOptions> apply)
    {
        apply(chartDisplaySettings.ForGraph(graphNumber));
        chartDisplaySettingsStore.Save(chartDisplaySettings);
        ApplyDisplayOverlayOptions(graphNumber);
    }

    private void ShowCurveVisibilityFlyout(int graphNumber, FrameworkElement anchor)
    {
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        StackPanel content = new() { Spacing = 4, MinWidth = 220 };
        content.Children.Add(new TextBlock
        {
            Text = "Видимые кривые",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        if (configuration.Curves.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Нет кривых. Выберите пресет или элементы схемы.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
        }
        else
        {
            StackPanel list = new() { Spacing = 2 };
            foreach (CurveDefinition curve in configuration.Curves)
            {
                CheckBox curveCheck = new()
                {
                    Content = string.IsNullOrWhiteSpace(curve.Units) ? curve.DisplayName : $"{curve.DisplayName} ({curve.Units})",
                    IsChecked = curve.Enabled,
                    MinWidth = 0,
                    Tag = curve.Id
                };
                curveCheck.Checked += (_, _) => SetCurveVisibility(graphNumber, curve.Id, true);
                curveCheck.Unchecked += (_, _) => SetCurveVisibility(graphNumber, curve.Id, false);
                list.Children.Add(curveCheck);
            }

            content.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320
            });
        }

        Flyout flyout = new() { Content = content };

        // Флайаут разворачивается в popup-слое окна, куда преобразование
        // содержимого не достаёт, поэтому масштаб задаётся его разметке —
        // так же, как пунктам меню.
        PopupScale.Apply(flyout);
        flyout.ShowAt(anchor);
    }

    private void SetCurveVisibility(int graphNumber, string curveId, bool visible)
    {
        ChartConfiguration configuration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        CurveDefinition? curve = configuration.Curves.FirstOrDefault(item => item.Id == curveId);
        if (curve is null || curve.Enabled == visible)
        {
            return;
        }

        curve.Enabled = visible;
        configuration.UpdateAxisConfiguration();
        RenderGraph(graphNumber);
    }

    private readonly record struct PresetMenuTag(PresetType Preset, SmtComputationMode? Mode);

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
        // Every data-source change funnels through here, so this is where a
        // configuration that no longer matches the tool loses its extra curves
        // (a T5 curve carried over to a four-transmitter source). Runs before the
        // schematic and legend read the configuration.
        chartWorkspace.DropUnsupportedCurvesForGraph(graphNumber);
        ChartConfiguration graphConfiguration = chartWorkspace.GetConfigurationForGraph(graphNumber);
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);

        if (chartDisplaySettings.ForGraph(graphNumber).ShowTopSchematic)
        {
            UpdateInlineSchematicVisual(graphNumber);
        }

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

        string recordPath = ResolveSessionRecordPath();
        return recordPath.Length > 0 ? recordPath + ".notes.jsonl" : string.Empty;
    }

    /// <summary>
    /// Файл записи текущей сессии — и пока прибор опрашивается, и после того,
    /// как его отключили.
    /// </summary>
    /// <remarks>
    /// Раньше путь выдавался только при подключённом приборе, поэтому после
    /// отключения привязка заметок терялась: принятые кадры оставались на
    /// графике, файл — на диске, а поставить заметку по только что записанным
    /// данным было нельзя, пока тот же файл не откроешь заново через меню.
    /// </remarks>
    private string ResolveSessionRecordPath()
    {
        if (serialRecordingEnabled && serialDeviceService.IsConnected)
        {
            SerialStatusSnapshot status = serialDeviceService.GetStatus();
            string activePath = status.RecordFilePath.Length > 0
                ? status.RecordFilePath
                : configuredAppendRecordPath;
            if (activePath.Length > 0)
            {
                sessionRecordFilePath = activePath;
                return activePath;
            }
        }

        // Прибор отключён, но его запись никуда не делась. Файл проверяется на
        // месте: его могли удалить или перенести, а заметки рядом с
        // несуществующим файлом не нужны.
        return sessionRecordFilePath.Length > 0 && File.Exists(sessionRecordFilePath)
            ? sessionRecordFilePath
            : string.Empty;
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

        // With a combined preset both axes carry a value, so the crosshair reports
        // the left and the right one, as legacy does through its Y2 field.
        SetSecondCursorValue(
            graphNumber,
            args.HasRightAxis,
            FormatCursorValue(args.YRight, configuration.RightAxisUnits));
    }

    private void SetSecondCursorValue(int graphNumber, bool hasRightAxis, string text)
    {
        TextBlock separator = graphNumber == 1 ? CursorY2Separator : Cursor2Y2Separator;
        TextBox valueBox = graphNumber == 1 ? CursorY2TextBox : Cursor2Y2TextBox;
        Visibility visibility = hasRightAxis ? Visibility.Visible : Visibility.Collapsed;
        separator.Visibility = visibility;
        valueBox.Visibility = visibility;
        valueBox.Text = hasRightAxis ? text : "—";
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

        SetSecondCursorValue(graphNumber, hasRightAxis: false, text: "—");
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
        ScaleMenuItem(setXFromItem);
        menu.Items.Add(setXFromItem);

        MenuFlyoutItem setXToItem = new()
        {
            Text = "Установить как X конечное",
            IsEnabled = args.HasCrosshair
        };
        setXToItem.Click += (_, _) => SetXRangeBoundaryFromCrosshair(graphNumber, setStart: false);
        ScaleMenuItem(setXToItem);
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
        ScaleMenuItem(addNoteItem);
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
        if (await dialog.ShowScaledAsync() != ContentDialogResult.Primary)
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
        ContentDialogResult result = await dialog.ShowScaledAsync();
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
            if (await confirmation.ShowScaledAsync() == ContentDialogResult.Primary)
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
        windowScaleService.Attach(notesJournalWindow, resizeWithScale: true);
        notesJournalWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Opens the published documentation in the default browser, matching legacy
    /// OpenDocumentation: the PDF lives next to the update descriptor in the same
    /// HTTP catalogue.
    /// </summary>
    private async void DocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DocumentationUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await ShowUpdateMessageAsync(
                "Документация недоступна",
                "Не удалось открыть документацию.\nПроверьте подключение к интернету и попробуйте снова.",
                compactCentered: true);
        }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        deviceLogWindow ??= new DeviceLogWindow();
        windowScaleService.Attach(deviceLogWindow, resizeWithScale: true);
        deviceLogWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    private void WindowScaleButton_Click(object sender, RoutedEventArgs e)
    {
        windowScaleWindow ??= new WindowScaleWindow(windowScaleService);
        windowScaleService.Attach(windowScaleWindow, resizeWithScale: true);
        windowScaleWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    private void WindowScaleService_ScaleChanged(object? sender, EventArgs e)
    {
        windowScaleSettingsStore.Save(windowScaleService.Settings);
        RebuildScaledMenus();
    }

    /// <summary>
    /// Пересобирает выпадающие меню под новый масштаб интерфейса.
    /// </summary>
    /// <remarks>
    /// Размер пунктов заложен в их разметку при построении, поэтому смена
    /// масштаба сама по себе меню не трогает: без пересборки они остались бы в
    /// размере, который был при запуске. Контекстное меню графика строится
    /// каждый раз заново и в пересборке не нуждается.
    /// </remarks>
    private void RebuildScaledMenus()
    {
        foreach ((int graphNumber, DropDownButton button) in presetDropDowns)
        {
            button.Flyout = BuildPresetFlyout(graphNumber);
        }

        foreach ((int graphNumber, DropDownButton button) in frequencyDropDowns)
        {
            button.Flyout = BuildFrequencyFlyout(graphNumber);
        }

        ScaleFileMenuItems();
    }

    /// <summary>
    /// Задаёт масштаб пунктам меню «Файл».
    /// </summary>
    /// <remarks>
    /// Пункты этого меню объявлены в разметке, а не построены кодом, поэтому
    /// они не проходят через <see cref="ScaleMenuItem"/> при создании и остались
    /// бы в системном размере — как и любое другое <c>MenuFlyout</c>.
    /// Заголовок «Файл» в масштабировании не нуждается: он часть содержимого
    /// главного окна.
    /// </remarks>
    private void ScaleFileMenuItems()
    {
        foreach (MenuFlyoutItemBase item in FileMenuBarItem.Items)
        {
            if (item is MenuFlyoutItem menuItem)
            {
                ScaleMenuItem(menuItem);
            }
        }
    }

    private void DeviceRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        deviceRegistryWindow ??= new DeviceRegistryWindow(deviceRegistryConnection);
        windowScaleService.Attach(deviceRegistryWindow, resizeWithScale: true);
        deviceRegistryWindow.ShowForOwner(WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Пункт «Загрузить в базу данных» появляется только тогда, когда есть что
    /// загружать: открыт файл данных или идёт запись через COM-порт.
    /// </summary>
    private void UpdateUploadMenuAvailability()
    {
        bool hasFile = chartWorkspace.Graph1Repository.HasData;
        bool isRecording = GetActiveRecordingPath().Length > 0;
        UploadDataFileMenuItem.Visibility = hasFile || isRecording
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetActiveRecordingPath()
    {
        SerialStatusSnapshot status = serialDeviceService.GetStatus();
        return string.IsNullOrWhiteSpace(status.RecordFilePath) ? string.Empty : status.RecordFilePath;
    }

    private async void UploadDataFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        UploadKind? kind = await AskDataFileKindAsync();
        if (kind is null)
        {
            return;
        }

        // Выгружается то, что пользователь видит. Пока идёт опрос и открыт файл,
        // существуют оба источника, и запись сессии не должна подменять собой
        // файл, который сейчас на графике.
        string recordingPath = GetActiveRecordingPath();
        bool useRecording = recordingPath.Length > 0
            && chartWorkspace.Graph1Source == Graph1DataSource.Device;
        string sourcePath = useRecording ? recordingPath : currentDataFilePath;

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            await ShowNotesMessageAsync(
                "Загрузка в базу",
                "Нет файла для загрузки: откройте файл данных или включите запись с прибора.");
            return;
        }

        // Файл выгружается ровно тем, чем он является на диске. Размер записи
        // складывается из кадра прибора — 240 байт у старой прошивки, 320 у
        // новой — и служебного заголовка `.DEV`.
        FileFormat sourceFormat = ToolDataFileReader.DetectFileFormat(sourcePath);
        if (sourceFormat == FileFormat.Unknown)
        {
            await ShowNotesMessageAsync(
                "Загрузка в базу",
                "Неподдерживаемый формат файла. Загружаются .bin, .biv и .DEV.");
            return;
        }

        if (!ToolDataFileReader.TryReadFileSignature(sourcePath, sourceFormat, out uint sourceSignature))
        {
            await ShowNotesMessageAsync("Загрузка в базу", "Не удалось прочитать сигнатуру файла.");
            return;
        }

        int physicalFrameSize = ToolDataFileReader.GetPhysicalFrameSize(
            sourceFormat,
            ToolDataAdapterRegistry.DecodeFrameSize(sourceSignature));
        byte[] content;
        int frameCount;
        try
        {
            content = ReadDataFileSnapshot(sourcePath, physicalFrameSize);
            frameCount = content.Length / physicalFrameSize;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowNotesMessageAsync("Загрузка в базу", $"Не удалось прочитать файл: {exception.Message}");
            return;
        }

        if (frameCount == 0)
        {
            await ShowNotesMessageAsync("Загрузка в базу", "В файле нет ни одного полного кадра.");
            return;
        }

        if (!TryDescribeDataFileDevice(
                content,
                sourceFormat,
                out int? serialNumber,
                out DeviceType deviceType,
                out int transmitterCount))
        {
            await ShowNotesMessageAsync(
                "Загрузка в базу",
                "Не удалось определить тип прибора по сигнатуре файла.");
            return;
        }

        string description = useRecording
            ? $"Запись с прибора: {Path.GetFileName(sourcePath)}, выгружено {frameCount} кадров на текущий момент. " +
              "Кадры, принятые позже, в эту копию не войдут."
            : $"Файл данных: {Path.GetFileName(sourcePath)}, {frameCount} кадров.";

        await deviceRegistryUploadFlow.RunAsync(
            Content.XamlRoot,
            new UploadSource(
                kind.Value,
                content,
                Path.GetExtension(sourcePath),
                serialNumber,
                deviceType,
                transmitterCount,
                description));
    }

    private async Task<UploadKind?> AskDataFileKindAsync()
    {
        RadioButton thermoprofileOption = new()
        {
            Content = UploadKind.Thermoprofile.ToDisplayName(),
            GroupName = "UploadKind",
            IsChecked = true
        };
        RadioButton kisOption = new()
        {
            Content = UploadKind.Kis.ToDisplayName(),
            GroupName = "UploadKind"
        };

        StackPanel panel = new() { Spacing = 8, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "Выберите раздел карточки прибора, в который загружается запись.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(thermoprofileOption);
        panel.Children.Add(kisOption);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Тип загружаемого файла",
            Content = panel,
            PrimaryButtonText = "Далее",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowScaledAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return kisOption.IsChecked == true ? UploadKind.Kis : UploadKind.Thermoprofile;
    }

    /// <summary>
    /// Читает файл целым числом физических кадров.
    ///
    /// Во время записи файл открыт на запись самим приложением, поэтому чтение
    /// идёт с разделением доступа, а хвост обрезается по границе кадра: иначе в
    /// базу мог бы уйти файл с половиной последней записи.
    /// </summary>
    private static byte[] ReadDataFileSnapshot(string path, int physicalFrameSize)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long completeLength = stream.Length - (stream.Length % physicalFrameSize);
        if (completeLength <= 0)
        {
            return [];
        }

        byte[] buffer = new byte[completeLength];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// Определяет прибор по сигнатуре первого кадра выгружаемого файла.
    ///
    /// Сигнатура содержит и тип с числом передатчиков, и номер прибора, поэтому
    /// метрология для этого не нужна: файл описывает себя сам. Смещение
    /// сигнатуры зависит от формата — у `.DEV` перед данными идёт служебный
    /// заголовок в 11 байт.
    /// </summary>
    private static bool TryDescribeDataFileDevice(
        byte[] content,
        FileFormat format,
        out int? serialNumber,
        out DeviceType deviceType,
        out int transmitterCount)
    {
        serialNumber = null;
        deviceType = DeviceType.Unknown;
        transmitterCount = 0;

        int signatureOffset = ToolDataFileReader.GetSignatureOffset(format);
        if (content.Length < signatureOffset + sizeof(uint))
        {
            return false;
        }

        uint fileSignature = BinaryPrimitives.ReadUInt32LittleEndian(
            content.AsSpan(signatureOffset, sizeof(uint)));
        if (fileSignature == 0)
        {
            return false;
        }

        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(fileSignature);
        deviceType = identity.DeviceType;
        transmitterCount = identity.TransmitterCount;
        serialNumber = identity.Number;
        return deviceType != DeviceType.Unknown;
    }

    private static void LogLoadedDataFile(string filePath, ToolDataRepository repository)
    {
        if (!repository.TryGetRecord(0, out var firstRecord))
        {
            return;
        }

        DeviceExchangeLog.BeginFileSession(filePath);
        DeviceExchangeLog.SetRawDataAvailable(false);
        DeviceExchangeLog.Append($"Файл загружен: {filePath}");
        DeviceExchangeLog.AppendDeviceIdentity(
            firstRecord.Signature,
            serial: 0,
            rxPosition: 0,
            sondeDiameterMillimeters: 0);
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

        chartWorkspace.ApplyConfigurationForGraph(
            graphNumber,
            configuration,
            ChartConfigurationSource.NoteSnapshot);
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
        await dialog.ShowScaledAsync();
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
        YZoomTarget target = GetEffectiveYZoomTarget(graphNumber);
        YZoomRequest yZoom = new(
            target,
            args.LeftYRange,
            args.RightYRange,
            args.LeftAxisBaseRange,
            args.RightAxisBaseRange);
        if (!viewport.ApplyDragZoom(repository.DataSize, args.StartFraction, args.EndFraction, yZoom))
        {
            SetStatus(viewport.IsFlowEnabled
                ? "Минимальный диапазон Y — 10 единиц"
                : "Минимальный диапазон X — 10 записей");
            return;
        }

        RenderGraph(graphNumber);
        SetStatus(target switch
        {
            YZoomTarget.LeftOnly => $"График {graphNumber}: zoom применён по левой оси Y",
            YZoomTarget.RightOnly => $"График {graphNumber}: zoom применён по правой оси Y",
            _ => $"График {graphNumber}: zoom применён"
        });
    }

    // ----- Selective Y-axis zoom ("Zoom оси Y") -----

    private void Graph1YZoomTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ChangeYZoomTarget(1, Graph1YZoomTargetComboBox.SelectedIndex);

    private void Graph2YZoomTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ChangeYZoomTarget(2, Graph2YZoomTargetComboBox.SelectedIndex);

    private void ChangeYZoomTarget(int graphNumber, int selectedIndex)
    {
        if (updatingRangeControls)
        {
            return;
        }

        yZoomTargets[graphNumber] = selectedIndex switch
        {
            1 => YZoomTarget.LeftOnly,
            2 => YZoomTarget.RightOnly,
            _ => YZoomTarget.Both
        };
        ApplyVerticalScrollTarget(graphNumber);
    }

    /// <summary>
    /// The stored choice only applies while the graph really shows two axes; a
    /// single-axis configuration always zooms the one axis it has.
    /// </summary>
    private YZoomTarget GetEffectiveYZoomTarget(int graphNumber)
    {
        return chartWorkspace.GetConfigurationForGraph(graphNumber).HasDualAxis
            ? yZoomTargets[graphNumber]
            : YZoomTarget.Both;
    }

    private void ApplyVerticalScrollTarget(int graphNumber)
    {
        LineChartControl control = graphNumber == 1 ? GraphControl : Graph2Control;
        control.VerticalScrollTarget = GetEffectiveYZoomTarget(graphNumber) switch
        {
            YZoomTarget.LeftOnly => ChartVerticalScrollTarget.LeftOnly,
            YZoomTarget.RightOnly => ChartVerticalScrollTarget.RightOnly,
            _ => ChartVerticalScrollTarget.Both
        };
    }

    // ----- Per-axis zoom undo ("−" next to each scroll bar) -----

    private void UndoZoomAxis(int graphNumber, bool isVertical)
    {
        activeChartNumber = graphNumber;
        ChartViewportState viewport = chartWorkspace.GetViewportForGraph(graphNumber);
        bool undone = isVertical ? viewport.TryUndoZoomY() : viewport.TryUndoZoomX();
        if (!undone)
        {
            return;
        }

        RenderGraph(graphNumber);
        SetStatus(isVertical
            ? $"График {graphNumber}: отменён шаг zoom по оси Y"
            : $"График {graphNumber}: отменён шаг zoom по оси X");
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

        // The render just measured the newly visible records, so a manual Y window
        // can now be fitted to them. It keeps the Y zoom — and with it the vertical
        // scroll bar — alive across a horizontal scroll. A second render only runs
        // when the window actually moved.
        LineChartControl control = graphNumber == 1 ? GraphControl : Graph2Control;
        if (viewport.ClampYRangesToData(control.GetAutoAxisRange(0), control.GetAutoAxisRange(1)))
        {
            RenderGraph(graphNumber);
        }

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
        UpdateYZoomTargetControls(graphNumber);
        UpdateXAxisSelectorControls(graphNumber);
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

    /// <summary>
    /// The axis selector is only meaningful with two Y axes, so it appears exactly
    /// while the configuration is dual-axis (legacy UpdateYZoomTargetVisibility).
    /// Callers must already hold <see cref="updatingRangeControls"/>.
    /// </summary>
    private void UpdateYZoomTargetControls(int graphNumber)
    {
        bool hasDualAxis = chartWorkspace.GetConfigurationForGraph(graphNumber).HasDualAxis;
        Visibility visibility = hasDualAxis ? Visibility.Visible : Visibility.Collapsed;
        TextBlock label = graphNumber == 1 ? Graph1YZoomTargetLabel : Graph2YZoomTargetLabel;
        ComboBox selector = graphNumber == 1 ? Graph1YZoomTargetComboBox : Graph2YZoomTargetComboBox;
        label.Visibility = visibility;
        selector.Visibility = visibility;
        selector.SelectedIndex = yZoomTargets[graphNumber] switch
        {
            YZoomTarget.LeftOnly => 1,
            YZoomTarget.RightOnly => 2,
            _ => 0
        };
        ApplyVerticalScrollTarget(graphNumber);
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

        await dialog.ShowScaledAsync();
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
        ContentDialogResult choice = await dialog.ShowScaledAsync();
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
        await dialog.ShowScaledAsync();
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
                ? "Для вывода кривых выберите необходимый пресет в выпадающем списке «Быстрые пресеты» или кликните на «Настройка кривых» в левом углу графика"
                : "Сначала откройте файл данных или включите прибор");
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
            ? BuildConnectedStatusInformation()
            : chartWorkspace.GetStatusFileInformation();

        // Здесь же, а не в отдельных местах: этот метод вызывается и при
        // открытии файла, и на каждый принятый кадр, и при смене состояния
        // связи — то есть ровно тогда, когда меняется наличие того, что можно
        // выгрузить.
        UpdateUploadMenuAvailability();
    }

    /// <summary>
    /// Строка состояния при подключённом приборе. Счётчик кадров всегда
    /// относится к сессии опроса, даже когда на графике показан открытый файл:
    /// опрос в этот момент идёт, и его нужно видеть. Открытый файл называется
    /// отдельно, чтобы одно не выдавалось за другое.
    /// </summary>
    private string BuildConnectedStatusInformation()
    {
        string deviceInformation =
            $"{serialDeviceService.ConnectedPort}  |  Кадров: {chartWorkspace.Graph1DeviceRepository.DataSize}";
        if (chartWorkspace.Graph1Source != Graph1DataSource.File)
        {
            return deviceInformation;
        }

        ToolDataRepository fileRepository = chartWorkspace.Graph1FileRepository;
        string fileName = string.IsNullOrWhiteSpace(fileRepository.LoadedFileName)
            ? "нет файла"
            : Path.GetFileName(fileRepository.LoadedFileName);
        return $"{deviceInformation}  |  На графике: {fileName} ({fileRepository.DataSize})";
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
        deviceLogWindow?.ClosePermanently();
        deviceLogWindow = null;
        windowScaleWindow?.ClosePermanently();
        windowScaleWindow = null;
        deviceRegistryWindow?.ClosePermanently();
        deviceRegistryWindow = null;
        windowScaleService.ScaleChanged -= WindowScaleService_ScaleChanged;
        serialDeviceService.Dispose();
        autoUpdateService.Dispose();
        autoUpdateCancellation.Dispose();
    }
}
