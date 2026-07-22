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
        PollIntervalTextBox.Text = settings.PollIntervalMilliseconds.ToString();
        RecordingCheckBox.IsChecked = settings.RecordingEnabled;
        AppendCheckBox.IsChecked = settings.AppendToExistingFile;
        ShowFullFileCheckBox.IsChecked = settings.ShowFullExistingFile;
        applyingSettings = false;
        UpdateRecordingControls(deviceService.GetStatus());
    }

    private void CaptureSettingsFromControls()
    {
        settings.LastPort = PortComboBox.SelectedItem as string ?? PortComboBox.Text ?? string.Empty;
        settings.PollIntervalMilliseconds = GetPollInterval();
        settings.RecordingEnabled = RecordingCheckBox.IsChecked == true;
        settings.AppendToExistingFile = AppendCheckBox.IsChecked == true;
        settings.ShowFullExistingFile = settings.AppendToExistingFile && ShowFullFileCheckBox.IsChecked == true;
    }

    private int GetPollInterval()
    {
        if (!int.TryParse(PollIntervalTextBox.Text, out int interval) || interval < 500)
        {
            interval = 2000;
        }

        interval = Math.Clamp(interval, 500, 30000);
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
            string selected = PortComboBox.SelectedItem as string ?? settings.LastPort;
            IReadOnlyList<string> ports = await Task.Run(SerialDeviceService.GetAvailablePorts);
            PortComboBox.ItemsSource = ports;
            if (!string.IsNullOrWhiteSpace(selected) && ports.Contains(selected, StringComparer.OrdinalIgnoreCase))
            {
                PortComboBox.SelectedItem = ports.First(port => port.Equals(selected, StringComparison.OrdinalIgnoreCase));
            }
            else if (ports.Count > 0)
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
        settings.PollIntervalMilliseconds = GetPollInterval();
        settingsStore.Save(settings);
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
        PortComboBox.IsEnabled = !busy && !deviceService.IsConnected;
        RefreshPortsButton.IsEnabled = !busy && !deviceService.IsConnected;
        ConnectButton.IsEnabled = !busy && !deviceService.IsConnected;
        DisconnectButton.IsEnabled = !busy && deviceService.IsConnected;
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
                SerialConnectionState.ConnectionError => "Ошибка подключения",
                _ => "Отключено"
            };
        }

        LastFrameTextBlock.Text = status.PacketsReceived.ToString();
        PacketsTextBlock.Text = $"{status.PacketsReceived} / {status.PacketErrors}";
        RecordedFramesTextBlock.Text = $"{status.RecordedFrames} кадров";
        UpdateRecordingControls(status);

        PortComboBox.IsEnabled = status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        RefreshPortsButton.IsEnabled = !refreshingPorts
            && status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        ConnectButton.IsEnabled = status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        DisconnectButton.IsEnabled = status.State == SerialConnectionState.Connected;
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
