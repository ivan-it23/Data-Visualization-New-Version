using System.Diagnostics;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Hardware.Serial;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class ConnectionSettingsWindow : Window
{
    private readonly SerialDeviceService deviceService;
    private readonly SerialConnectionSettingsStore settingsStore;
    private readonly Action<SerialConnectionSettings> prepareSession;
    private SerialConnectionSettings settings;
    private bool allowPermanentClose;
    private bool activated;
    private bool applyingSettings = true;
    private bool refreshingPorts;

    internal ConnectionSettingsWindow(
        SerialDeviceService deviceService,
        SerialConnectionSettingsStore settingsStore,
        Action<SerialConnectionSettings> prepareSession)
    {
        this.deviceService = deviceService;
        this.settingsStore = settingsStore;
        this.prepareSession = prepareSession;
        settings = settingsStore.Load();

        InitializeComponent();
        AppWindow.Resize(new SizeInt32(600, 680));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        ApplySettingsToControls();
        deviceService.StatusChanged += DeviceService_StatusChanged;
        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        SecondaryWindowIntegration.ConfigureForOwner(windowHandle, ownerHandle);

        if (!activated)
        {
            Activate();
            activated = true;
        }
        else
        {
            AppWindow.Show();
            Activate();
        }

        SecondaryWindowIntegration.RestoreIfMinimized(windowHandle);
        SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle);
        DispatcherQueue.TryEnqueue(() =>
            SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle));

        _ = RefreshPortsAsync();
        UpdateStatus(deviceService.GetStatus());
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        deviceService.StatusChanged -= DeviceService_StatusChanged;
        Close();
    }

    private void ApplySettingsToControls()
    {
        applyingSettings = true;
        // The interval reflects the current session, not a persisted value.
        PollIntervalTextBox.Text = deviceService.PollIntervalMilliseconds.ToString();
        RecordingCheckBox.IsChecked = settings.RecordingEnabled;
        AppendCheckBox.IsChecked = settings.AppendToExistingFile;
        ShowFullFileCheckBox.IsChecked = settings.ShowFullExistingFile;
        applyingSettings = false;
        UpdateRecordingControls(deviceService.GetStatus());
    }

    private void CaptureSettingsFromControls()
    {
        // Only an actual selected port overwrites the saved value. When the
        // combo has no selection yet (e.g. the port list is still loading, or
        // the saved port is offline and no explicit choice was made), the
        // previously saved LastPort is preserved instead of being wiped.
        string selectedPort = PortComboBox.SelectedItem as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedPort))
        {
            settings.LastPort = selectedPort;
        }

        settings.PollIntervalMilliseconds = GetPollInterval();
        settings.RecordingEnabled = RecordingCheckBox.IsChecked == true;
        settings.AppendToExistingFile = AppendCheckBox.IsChecked == true;
        settings.ShowFullExistingFile = settings.AppendToExistingFile && ShowFullFileCheckBox.IsChecked == true;
    }

    private int GetPollInterval()
    {
        if (!int.TryParse(PollIntervalTextBox.Text, out int interval))
        {
            interval = 2000;
        }

        // 700 ms is the hard minimum: below it the device cannot deliver frames
        // in time. There is no persistence — this only shapes the session value.
        interval = Math.Clamp(interval, 700, 30000);
        PollIntervalTextBox.Text = interval.ToString();
        return interval;
    }

    private void SaveSettings()
    {
        CaptureSettingsFromControls();
        settingsStore.Save(settings);
    }

    private async Task RefreshPortsAsync()
    {
        if (refreshingPorts)
        {
            return;
        }

        refreshingPorts = true;
        RefreshPortsButton.IsEnabled = false;
        ConnectionStateTextBlock.Text = "Поиск COM-портов...";
        try
        {
            // Prefer the current on-screen selection, otherwise the saved port.
            string desired = PortComboBox.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(desired))
            {
                desired = settings.LastPort;
            }

            IReadOnlyList<string> ports = await Task.Run(SerialDeviceService.GetAvailablePorts);

            // Keep the desired port present and selected even when it is not
            // currently connected, so the user's port is never silently replaced
            // by the first available one. Reconnecting the device and pressing
            // "Подключить" then works against the correct port.
            List<string> items = ports.ToList();
            bool desiredOffline = !string.IsNullOrWhiteSpace(desired)
                && !items.Contains(desired, StringComparer.OrdinalIgnoreCase);
            if (desiredOffline)
            {
                items.Insert(0, desired);
            }

            PortComboBox.ItemsSource = items;
            if (!string.IsNullOrWhiteSpace(desired))
            {
                PortComboBox.SelectedItem = items.First(port => port.Equals(desired, StringComparison.OrdinalIgnoreCase));
            }
            else if (items.Count > 0)
            {
                PortComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            refreshingPorts = false;
            RefreshPortsButton.IsEnabled = true;
            UpdateStatus(deviceService.GetStatus());
        }
    }

    private async void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPortsAsync();
    }

    private async void BrowseRecordPathButton_Click(object sender, RoutedEventArgs e)
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        if (AppendCheckBox.IsChecked == true)
        {
            FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".bin");
            InitializeWithWindow.Initialize(picker, handle);
            Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                settings.ExistingRecordFile = file.Path;
            }
        }
        else
        {
            FolderPicker picker = new() { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, handle);
            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                settings.RecordFolder = folder.Path;
            }
        }

        SaveSettings();
        UpdateRecordingControls(deviceService.GetStatus());
    }

    private void RecordingOptionChanged(object sender, RoutedEventArgs e)
    {
        if (applyingSettings || ShowFullFileCheckBox is null || BrowseRecordPathButton is null)
        {
            return;
        }

        SaveSettings();
        UpdateRecordingControls(deviceService.GetStatus());

        if (deviceService.IsConnected
            && (!settings.AppendToExistingFile || File.Exists(settings.ExistingRecordFile)))
        {
            deviceService.ConfigureRecording(
                settings.RecordingEnabled,
                settings.RecordFolder,
                settings.AppendToExistingFile ? settings.ExistingRecordFile : string.Empty);
        }
    }

    private void PollIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Session-only: apply to the running device, never persist.
        settings.PollIntervalMilliseconds = GetPollInterval();
        deviceService.SetPollInterval(settings.PollIntervalMilliseconds);
    }

    private async void OpenRecordFolderButton_Click(object sender, RoutedEventArgs e)
    {
        SerialStatusSnapshot status = deviceService.GetStatus();
        string folder = !string.IsNullOrWhiteSpace(status.RecordFilePath)
            ? Path.GetDirectoryName(status.RecordFilePath) ?? string.Empty
            : settings.AppendToExistingFile
                ? Path.GetDirectoryName(settings.ExistingRecordFile) ?? string.Empty
                : settings.RecordFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync("Папка записи недоступна.");
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (string.IsNullOrWhiteSpace(settings.LastPort))
        {
            await ShowMessageAsync("Выберите COM-порт.");
            return;
        }

        if (settings.RecordingEnabled && settings.AppendToExistingFile && !File.Exists(settings.ExistingRecordFile))
        {
            await ShowMessageAsync("Выберите существующий файл для дозаписи.");
            return;
        }

        try
        {
            SetBusy(true);
            ConnectionStateTextBlock.Text = "Подключение...";
            prepareSession(settings);
            deviceService.ConfigureRecording(
                settings.RecordingEnabled,
                settings.RecordFolder,
                settings.AppendToExistingFile ? settings.ExistingRecordFile : string.Empty);
            SerialOperationResult result = await deviceService.ConnectAsync(settings.LastPort);
            if (result == SerialOperationResult.Success)
            {
                await deviceService.StartPollingAsync(settings.PollIntervalMilliseconds);
            }
            else
            {
                await ShowMessageAsync(SerialDeviceService.ResultToDisplayText(result));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowMessageAsync(exception.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateStatus(deviceService.GetStatus());
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        await deviceService.DisconnectAsync();
        SetBusy(false);
        UpdateStatus(deviceService.GetStatus());
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        AppWindow.Hide();
    }

    private void SetBusy(bool busy)
    {
        PortComboBox.IsEnabled = !busy && !deviceService.IsConnectionRequested;
        RefreshPortsButton.IsEnabled = !busy && !deviceService.IsConnectionRequested;
        ConnectButton.IsEnabled = !busy && !deviceService.IsConnectionRequested;
        DisconnectButton.IsEnabled = !busy && deviceService.IsConnectionRequested;
    }

    private void DeviceService_StatusChanged(object? sender, SerialStatusChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateStatus(e.Status));
    }

    private void UpdateStatus(SerialStatusSnapshot status)
    {
        if (!refreshingPorts)
        {
            ConnectionStateTextBlock.Text = status.State switch
            {
                SerialConnectionState.Connecting => "Подключение...",
                SerialConnectionState.Connected when status.IsPolling => "Опрос...",
                SerialConnectionState.Connected => "Подключено",
                SerialConnectionState.Disconnected when status.ConnectionRequested => "Переподключение...",
                SerialConnectionState.ConnectionError => "Ошибка подключения",
                _ => "Отключено"
            };
        }

        LastFrameTextBlock.Text = status.PacketsReceived.ToString();
        PacketsTextBlock.Text = $"{status.PacketsReceived} / {status.PacketErrors}";
        RecordedFramesTextBlock.Text = $"{status.RecordedFrames} кадров";
        UpdateRecordingControls(status);

        PortComboBox.IsEnabled = !status.ConnectionRequested
            && status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        RefreshPortsButton.IsEnabled = !refreshingPorts
            && !status.ConnectionRequested
            && status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        ConnectButton.IsEnabled = !status.ConnectionRequested
            && status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        DisconnectButton.IsEnabled = status.ConnectionRequested
            && status.State != SerialConnectionState.Connecting;
    }

    private void UpdateRecordingControls(SerialStatusSnapshot status)
    {
        bool append = AppendCheckBox.IsChecked == true;
        ShowFullFileCheckBox.IsEnabled = append;
        BrowseRecordPathButton.Content = append ? "Файл..." : "Папка...";

        if (!string.IsNullOrWhiteSpace(status.RecordFilePath))
        {
            RecordFileTextBlock.Text = Path.GetFileName(status.RecordFilePath);
        }
        else if (append)
        {
            RecordFileTextBlock.Text = string.IsNullOrWhiteSpace(settings.ExistingRecordFile)
                ? "Выберите файл для дозаписи"
                : Path.GetFileName(settings.ExistingRecordFile);
        }
        else
        {
            RecordFileTextBlock.Text = "Не записывается";
        }
    }

    private async Task ShowMessageAsync(string message)
    {
        ContentDialog dialog = new()
        {
            Title = "Подключение к прибору",
            Content = message,
            CloseButtonText = "Закрыть",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowPermanentClose)
        {
            return;
        }

        SaveSettings();
        args.Cancel = true;
        sender.Hide();
    }
}
