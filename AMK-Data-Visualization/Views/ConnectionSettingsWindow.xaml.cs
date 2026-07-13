using System.Runtime.InteropServices;
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
    private const int WindowOwnerIndex = -8;
    private readonly SerialDeviceService deviceService;
    private readonly SerialConnectionSettingsStore settingsStore;
    private readonly Action<SerialConnectionSettings> prepareSession;
    private SerialConnectionSettings settings;
    private bool allowPermanentClose;
    private bool activated;

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
        AppWindow.Resize(new SizeInt32(620, 820));
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
        if (ownerHandle != IntPtr.Zero)
        {
            SetWindowLongPtr(windowHandle, WindowOwnerIndex, ownerHandle);
        }

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
        PollIntervalNumberBox.Value = settings.PollIntervalMilliseconds;
        RecordingCheckBox.IsChecked = settings.RecordingEnabled;
        AppendCheckBox.IsChecked = settings.AppendToExistingFile;
        ShowFullFileCheckBox.IsChecked = settings.ShowFullExistingFile;
        ShowFullFileCheckBox.IsEnabled = settings.AppendToExistingFile;
        RecordPathTextBox.Text = settings.AppendToExistingFile
            ? settings.ExistingRecordFile
            : settings.RecordFolder;
    }

    private void CaptureSettingsFromControls()
    {
        settings.LastPort = PortComboBox.SelectedItem as string ?? PortComboBox.Text ?? string.Empty;
        settings.PollIntervalMilliseconds = Math.Clamp(
            double.IsNaN(PollIntervalNumberBox.Value) ? 2000 : (int)PollIntervalNumberBox.Value,
            500,
            30000);
        settings.RecordingEnabled = RecordingCheckBox.IsChecked == true;
        settings.AppendToExistingFile = AppendCheckBox.IsChecked == true;
        settings.ShowFullExistingFile = settings.AppendToExistingFile && ShowFullFileCheckBox.IsChecked == true;
        if (settings.AppendToExistingFile)
        {
            settings.ExistingRecordFile = RecordPathTextBox.Text.Trim();
        }
        else
        {
            settings.RecordFolder = RecordPathTextBox.Text.Trim();
        }
    }

    private async Task RefreshPortsAsync()
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

        OperationMessageTextBlock.Text = ports.Count == 0 ? "COM-порты не найдены" : $"Найдено портов: {ports.Count}";
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
                RecordPathTextBox.Text = file.Path;
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
                RecordPathTextBox.Text = folder.Path;
            }
        }
    }

    private void RecordingOptionChanged(object sender, RoutedEventArgs e)
    {
        if (ShowFullFileCheckBox is null || RecordPathTextBox is null)
        {
            return;
        }

        bool append = AppendCheckBox.IsChecked == true;
        ShowFullFileCheckBox.IsEnabled = append;
        RecordPathTextBox.PlaceholderText = append ? "Файл для продолжения записи" : "Папка для нового файла записи";
        RecordPathTextBox.Text = append ? settings.ExistingRecordFile : settings.RecordFolder;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureSettingsFromControls();
        if (string.IsNullOrWhiteSpace(settings.LastPort))
        {
            OperationMessageTextBlock.Text = "Выберите COM-порт";
            return;
        }

        if (settings.RecordingEnabled && settings.AppendToExistingFile && !File.Exists(settings.ExistingRecordFile))
        {
            OperationMessageTextBlock.Text = "Выберите существующий файл для дозаписи";
            return;
        }

        try
        {
            settingsStore.Save(settings);
            SetBusy(true);
            OperationMessageTextBlock.Text = $"Подключение к {settings.LastPort}...";
            prepareSession(settings);
            deviceService.ConfigureRecording(
                settings.RecordingEnabled,
                settings.RecordFolder,
                settings.AppendToExistingFile ? settings.ExistingRecordFile : string.Empty);
            SerialOperationResult result = await deviceService.ConnectAsync(settings.LastPort);
            if (result == SerialOperationResult.Success)
            {
                await deviceService.StartPollingAsync(settings.PollIntervalMilliseconds);
                OperationMessageTextBlock.Text = "Прибор подключён, опрос запущен";
            }
            else
            {
                OperationMessageTextBlock.Text = SerialDeviceService.ResultToDisplayText(result);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OperationMessageTextBlock.Text = exception.Message;
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
        OperationMessageTextBlock.Text = "Прибор отключён";
        SetBusy(false);
        UpdateStatus(deviceService.GetStatus());
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
    }

    private void SetBusy(bool busy)
    {
        PortComboBox.IsEnabled = !busy && !deviceService.IsConnected;
        ConnectButton.IsEnabled = !busy && !deviceService.IsConnected;
        DisconnectButton.IsEnabled = !busy && deviceService.IsConnected;
    }

    private void DeviceService_StatusChanged(object? sender, SerialStatusChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateStatus(e.Status));
    }

    private void UpdateStatus(SerialStatusSnapshot status)
    {
        ConnectionStateTextBlock.Text = status.State switch
        {
            SerialConnectionState.Connecting => "Подключение...",
            SerialConnectionState.Connected when status.IsPolling => $"Подключено к {status.PortName}, опрос включён",
            SerialConnectionState.Connected => $"Подключено к {status.PortName}",
            SerialConnectionState.ConnectionError => "Ошибка подключения",
            _ => "Отключено"
        };
        LastFrameTextBlock.Text = status.PacketsReceived > 0 ? status.LastFrameNumber.ToString() : "—";
        PacketsTextBlock.Text = $"{status.PacketsReceived} / {status.PacketErrors}";
        RecordedFramesTextBlock.Text = status.RecordedFrames.ToString();
        RecordFileTextBlock.Text = string.IsNullOrWhiteSpace(status.RecordFilePath)
            ? "Файл записи будет создан после получения первого кадра"
            : status.RecordFilePath;
        DeviceInfoTextBlock.Text = status.Metrology is not null
            ? $"Сигнатура {status.Metrology.Signature}, № {status.Metrology.SerialNumber}, Ø {status.Metrology.DiameterMillimeters} мм"
            : status.DeviceSignature is uint signature
                ? $"Сигнатура {signature}"
                : "—";
        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            OperationMessageTextBlock.Text = status.LastError;
        }

        PortComboBox.IsEnabled = status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        ConnectButton.IsEnabled = status.State is SerialConnectionState.Disconnected or SerialConnectionState.ConnectionError;
        DisconnectButton.IsEnabled = status.State == SerialConnectionState.Connected;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowPermanentClose)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);
}
