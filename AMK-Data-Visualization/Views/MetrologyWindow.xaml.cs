using System.Globalization;
using System.Runtime.InteropServices;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;
using AMK.DataVisualization.DeviceRegistry;
using AMK.DataVisualization.Hardware.Serial;
using AMK.DataVisualization.Metrology;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class MetrologyWindow : Window
{
    private const int WindowWidth = 1220;
    private const int WindowHeight = 642;
    private const double RequiredContentWidth = 1204.0;
    private const double RequiredContentHeight = 604.0;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private readonly SerialDeviceService serialDeviceService;
    private readonly MetrologySession metrologySession;
    private readonly Action metrologyUpdated;
    private readonly DeviceRegistryUploadFlow uploadFlow;
    private readonly TextBox[] l1TextBoxes;
    private readonly TextBox[] l2TextBoxes;
    private readonly TextBox[,] airTextBoxes;
    private readonly TextBox[,] minimumTextBoxes;
    private readonly TextBox[,] airAttenuationTextBoxes;
    private IntPtr currentOwnerHandle;
    private bool allowPermanentClose;
    private bool activated;
    private bool initializedForm;

    /// <summary>
    /// Метрология, находящаяся сейчас в окне, получена из зонда после
    /// автокалибровки нулей воздуха. От этого зависит раздел карточки прибора,
    /// в который уходит файл: «Калибровка» вместо «Метрология».
    /// </summary>
    private bool calibratedInSession;

    /// <summary>
    /// Поколение прошивки, под которое сейчас настроены поля окна. Нужно,
    /// чтобы не перезаписывать введённое пользователем при каждом показе окна.
    /// </summary>
    private bool appliedExtendedFirmware;

    internal MetrologyWindow(
        SerialDeviceService serialDeviceService,
        MetrologySession metrologySession,
        DeviceRegistryUploadFlow uploadFlow,
        Action metrologyUpdated)
    {
        this.serialDeviceService = serialDeviceService;
        this.metrologySession = metrologySession;
        this.uploadFlow = uploadFlow;
        this.metrologyUpdated = metrologyUpdated;
        InitializeComponent();
        Activated += MetrologyWindow_Activated;

        l1TextBoxes = [L1T1TextBox, L1T2TextBox, L1T3TextBox, L1T4TextBox, L1T5TextBox];
        l2TextBoxes = [L2T1TextBox, L2T2TextBox, L2T3TextBox, L2T4TextBox, L2T5TextBox];
        airTextBoxes = new[,]
        {
            { AirF1T1TextBox, AirF1T2TextBox, AirF1T3TextBox, AirF1T4TextBox, AirF1T5TextBox },
            { AirF2T1TextBox, AirF2T2TextBox, AirF2T3TextBox, AirF2T4TextBox, AirF2T5TextBox }
        };
        minimumTextBoxes = new[,]
        {
            { MinF1T1TextBox, MinF1T2TextBox, MinF1T3TextBox, MinF1T4TextBox, MinF1T5TextBox },
            { MinF2T1TextBox, MinF2T2TextBox, MinF2T3TextBox, MinF2T4TextBox, MinF2T5TextBox }
        };
        airAttenuationTextBoxes = new[,]
        {
            { AirAttF1T1TextBox, AirAttF1T2TextBox, AirAttF1T3TextBox, AirAttF1T4TextBox, AirAttF1T5TextBox },
            { AirAttF2T1TextBox, AirAttF2T2TextBox, AirAttF2T3TextBox, AirAttF2T4TextBox, AirAttF2T5TextBox }
        };

        GeometryReadOnlyCheckBox.IsChecked = true;
        UpdateGeometryEditingState();

        TypeComboBox.Items.Add("Автономный");
        TypeComboBox.Items.Add("LWD");
        TypeComboBox.Items.Add("Картограф");
        foreach (int count in new[] { 3, 4, 5 })
        {
            TransmitterCountComboBox.Items.Add(count.ToString());
        }

        for (int modification = 1; modification <= 9; modification++)
        {
            ModificationComboBox.Items.Add(modification.ToString());
        }

        RxPositionComboBox.Items.Add("0 — R1→T1");
        RxPositionComboBox.Items.Add("1 — R1→T2");
        TypeComboBox.SelectedIndex = 0;
        TransmitterCountComboBox.SelectedIndex = 2;
        ModificationComboBox.SelectedIndex = 0;
        RxPositionComboBox.SelectedIndex = 0;

        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }

        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        currentOwnerHandle = ownerHandle;
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        SecondaryWindowIntegration.ConfigureForOwner(windowHandle, ownerHandle);

        if (!initializedForm)
        {
            FillForm(metrologySession.TryGet(out MetrologyData initialData) ? initialData : default);
            initializedForm = true;
        }

        UpdateConnectionControls();
        if (!activated)
        {
            Activate();
            activated = true;
        }
        else
        {
            AppWindow.Show();
        }

        ResizeForCurrentScale(windowHandle);

        SecondaryWindowIntegration.RestoreIfMinimized(windowHandle);

        SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle);
        DispatcherQueue.TryEnqueue(() =>
        {
            ResizeForCurrentScale(windowHandle);
            SecondaryWindowIntegration.CenterOverOwner(windowHandle, ownerHandle);
        });
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        Close();
    }

    public void RefreshConnectionState()
    {
        UpdateConnectionControls();
    }

    /// <summary>
    /// Выбирает сигнатуру, по которой окно показывает идентификацию зонда и
    /// поколение прошивки.
    /// </summary>
    /// <param name="metrologySignature">Сигнатура из самой структуры метрологии.</param>
    /// <param name="fromDevice">
    /// Идентификация взята не из метрологии, а из того, что отдаёт прибор.
    /// </param>
    /// <remarks>
    /// Обычно источник один — сигнатура метрологии, и она же описывает
    /// раскладку самой структуры. Но у прибора с неразмеченной
    /// энергонезависимой памятью на её месте мусор, и его расшифровка выдавала
    /// тип, число передатчиков и номер, которых у прибора нет: при
    /// подключённом пятипередатчиковом приборе окно показывало «Автономный
    /// 4 Tx», а поля новой прошивки прятались.
    ///
    /// Поэтому: сигнатура метрологии принимается, только если она разбирается
    /// в поддерживаемый прибор; иначе берётся то, что приложение знает о самом
    /// приборе — прежде всего сигнатура принятых кадров. Пользователь видит
    /// настоящий прибор, а запись метрологии заносит в него уже правильную
    /// сигнатуру.
    /// </remarks>
    private uint ResolveIdentitySignature(uint metrologySignature, out bool fromDevice)
    {
        fromDevice = false;
        if (ToolSignature.TryDecode(metrologySignature, out _))
        {
            return metrologySignature;
        }

        if (serialDeviceService.ResolvedDeviceSignature is uint deviceSignature)
        {
            fromDevice = true;
            return deviceSignature;
        }

        return metrologySignature;
    }

    /// <summary>
    /// Готовит воздушную поправку затухания к показу в поле.
    /// </summary>
    /// <remarks>
    /// В приборе с неразмеченной энергонезависимой памятью на этом месте лежат
    /// байты <c>0xFF</c>, а четыре таких байта как <c>float</c> дают NaN. По
    /// умолчанию он выводится как «не число» и выглядит отказом приложения,
    /// хотя означает ровно одно: поправка в приборе не задана. Пустое поле
    /// говорит то же самое и совпадает с остальными полями окна, где пустое
    /// значение означает «оставить как есть».
    /// </remarks>
    private static string FormatAirAttenuation(float value)
    {
        return float.IsFinite(value)
            ? value.ToString("0.######", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private void FillForm(MetrologyData data)
    {
        for (int transmitter = 0; transmitter < BinaryLayoutConstants.MaxTransmitterCount; transmitter++)
        {
            l1TextBoxes[transmitter].Text = data.GetL1(transmitter).ToString();
            l2TextBoxes[transmitter].Text = data.GetL2(transmitter).ToString();
            for (int frequency = 0; frequency < BinaryLayoutConstants.FrequencyCount; frequency++)
            {
                airTextBoxes[frequency, transmitter].Text = data.GetAirZz(frequency, transmitter).ToString();
                minimumTextBoxes[frequency, transmitter].Text = data.GetAirZzAmplitude(frequency, transmitter).ToString();
            }
        }

        Frequency1TextBox.Text = data.GetFrequency(0).ToString();
        Frequency2TextBox.Text = data.GetFrequency(1).ToString();
        SerialTextBox.Text = data.Serial.ToString();
        RxPositionComboBox.SelectedIndex = data.RxPosition == 1 ? 1 : 0;

        uint identitySignature = ResolveIdentitySignature(data.Signature, out bool identityFromDevice);
        MetrologySignatureInfo signature = MetrologySignature.Decode(identitySignature);
        TypeComboBox.SelectedIndex = signature.TypeClass switch
        {
            2 => 1,
            3 => 2,
            _ => 0
        };
        TransmitterCountComboBox.SelectedIndex = signature.TransmitterCount switch
        {
            3 => 0,
            4 => 1,
            _ => 2
        };
        ModificationComboBox.SelectedIndex = Math.Clamp(signature.Modification, 1, 9) - 1;
        NumberTextBox.Text = signature.Number.ToString();
        SignatureBinaryTextBox.Text = MetrologySignature.ToBinaryString(data.Signature);
        SignaturePaletteTextBox.Text = signature.Palette.ToString();

        RefreshFirmwareDependentControls(data, force: true);
    }

    /// <summary>
    /// Приводит поля, зависящие от поколения прошивки, к текущему состоянию.
    /// </summary>
    /// <remarks>
    /// Воздушные поправки затухания появились только в новой прошивке: у
    /// старой на их месте служебная область, смысл которой приложению
    /// неизвестен. Показывать там числа означало бы выдавать чужие байты за
    /// калибровку, поэтому поля гасятся и остаются пустыми.
    ///
    /// Метод вызывается и при открытии окна: если метрология прибора не
    /// размечена, поколение известно только по его кадрам, а первый кадр может
    /// прийти уже после чтения метрологии. Значения перезаписываются лишь при
    /// смене поколения — иначе несохранённая правка пользователя пропадала бы
    /// при каждом показе окна.
    /// </remarks>
    private void RefreshFirmwareDependentControls(MetrologyData data, bool force)
    {
        MetrologySignatureInfo signature = MetrologySignature.Decode(
            ResolveIdentitySignature(data.Signature, out bool identityFromDevice));
        bool extendedFirmware = signature.Generation == FirmwareGeneration.Extended;
        string firmwareName = extendedFirmware ? "новая" : "старая";
        FirmwareGenerationTextBox.Text = identityFromDevice
            ? $"{firmwareName}, кадр {signature.FrameSize} Б *"
            : $"{firmwareName}, кадр {signature.FrameSize} Б";
        ToolTipService.SetToolTip(
            FirmwareGenerationTextBox,
            identityFromDevice
                ? "Метрология в приборе не размечена: её сигнатура не опознаёт прибор. "
                  + "Тип, число передатчиков, модификация и серийный номер показаны по кадрам прибора "
                  + "и будут записаны в него кнопкой «Записать в зонд»."
                : "Поколение прошивки определено по сигнатуре метрологии.");

        if (!force && appliedExtendedFirmware == extendedFirmware)
        {
            return;
        }

        appliedExtendedFirmware = extendedFirmware;
        AirAttF1Header.Opacity = extendedFirmware ? 1.0 : 0.5;
        AirAttF2Header.Opacity = extendedFirmware ? 1.0 : 0.5;
        for (int transmitter = 0; transmitter < BinaryLayoutConstants.MaxTransmitterCount; transmitter++)
        {
            for (int frequency = 0; frequency < BinaryLayoutConstants.FrequencyCount; frequency++)
            {
                TextBox box = airAttenuationTextBoxes[frequency, transmitter];
                box.IsEnabled = extendedFirmware;
                box.Text = extendedFirmware
                    ? FormatAirAttenuation(data.GetAirAttDb(frequency, transmitter))
                    : string.Empty;
            }
        }
    }

    private bool TryReadForm(out MetrologyData data, out string error)
    {
        data = metrologySession.TryGet(out MetrologyData current) ? current : default;
        error = string.Empty;
        for (int transmitter = 0; transmitter < BinaryLayoutConstants.MaxTransmitterCount; transmitter++)
        {
            if (!TryReadUShort(l1TextBoxes[transmitter], $"L1 T{transmitter + 1}", data.GetL1(transmitter), out ushort l1, out error)
                || !TryReadUShort(l2TextBoxes[transmitter], $"L2 T{transmitter + 1}", data.GetL2(transmitter), out ushort l2, out error))
            {
                return false;
            }

            data.SetL1(transmitter, l1);
            data.SetL2(transmitter, l2);
            for (int frequency = 0; frequency < BinaryLayoutConstants.FrequencyCount; frequency++)
            {
                string suffix = $"F{frequency + 1} T{transmitter + 1}";
                if (!TryReadShort(airTextBoxes[frequency, transmitter], $"Поправка фазы {suffix}", data.GetAirZz(frequency, transmitter), out short air, out error)
                    || !TryReadShort(minimumTextBoxes[frequency, transmitter], $"Порог амплитуды {suffix}", data.GetAirZzAmplitude(frequency, transmitter), out short minimum, out error))
                {
                    return false;
                }

                data.SetAirZz(frequency, transmitter, air);
                data.SetAirZzAmplitude(frequency, transmitter, minimum);
            }
        }

        // Поправки затухания пишутся только приборам новой прошивки. У старой
        // по этим смещениям служебная область: запись туда испортила бы
        // содержимое, смысл которого приложению неизвестен.
        if (MetrologySignature.Decode(ResolveIdentitySignature(data.Signature, out _)).Generation
            == FirmwareGeneration.Extended)
        {
            for (int transmitter = 0; transmitter < BinaryLayoutConstants.MaxTransmitterCount; transmitter++)
            {
                for (int frequency = 0; frequency < BinaryLayoutConstants.FrequencyCount; frequency++)
                {
                    // В прибор не уходит нечисловое значение. Незаданная
                    // поправка из неразмеченной памяти прибора — это NaN, и
                    // записанный обратно он сделал бы бортовой расчёт
                    // затухания нечисловым. Ноль означает «поправки нет» и
                    // является единственным осмысленным значением по умолчанию.
                    float stored = data.GetAirAttDb(frequency, transmitter);
                    float fallback = float.IsFinite(stored) ? stored : 0.0f;
                    if (!TryReadFloat(
                            airAttenuationTextBoxes[frequency, transmitter],
                            $"Поправка затухания F{frequency + 1} T{transmitter + 1}",
                            fallback,
                            out float airAttenuation,
                            out error))
                    {
                        return false;
                    }

                    data.SetAirAttDb(
                        frequency,
                        transmitter,
                        float.IsFinite(airAttenuation) ? airAttenuation : 0.0f);
                }
            }
        }

        if (!TryReadUShort(Frequency1TextBox, "F1", data.GetFrequency(0), out ushort frequency1, out error)
            || !TryReadUShort(Frequency2TextBox, "F2", data.GetFrequency(1), out ushort frequency2, out error))
        {
            return false;
        }

        data.SetFrequency(0, frequency1);
        data.SetFrequency(1, frequency2);
        string serialText = SerialTextBox.Text.Trim();
        if (serialText.Length > 0)
        {
            if (!int.TryParse(serialText, out int serial) || serial < 0)
            {
                error = "Корпус № должен быть целым числом от 0 до 2147483647.";
                return false;
            }

            data.Serial = (uint)serial;
        }

        int number = 0;
        string numberText = NumberTextBox.Text.Trim();
        if (numberText.Length > 0 && (!int.TryParse(numberText, out number) || number is < 0 or > 999))
        {
            error = "Серийный № должен быть целым числом от 0 до 999.";
            return false;
        }

        int typeClass = TypeComboBox.SelectedIndex is >= 0 and <= 2 ? TypeComboBox.SelectedIndex + 1 : 1;
        int transmitterCount = TransmitterCountComboBox.SelectedIndex is >= 0 and <= 2
            ? TransmitterCountComboBox.SelectedIndex + 3
            : BinaryLayoutConstants.MaxTransmitterCount;
        int modification = ModificationComboBox.SelectedIndex >= 0 ? ModificationComboBox.SelectedIndex + 1 : 1;

        // Сигнатура пересобирается на той же основе, что показана в окне.
        // Если метрология прибора не размечена, основой служит сигнатура его
        // кадров: иначе запись сохранила бы в приборе мусор из старших
        // разрядов вместе с правильными полями.
        uint signatureBase = ResolveIdentitySignature(data.Signature, out _);
        data.Signature = MetrologySignature.Build(
            typeClass,
            transmitterCount,
            modification,
            number,
            signatureBase);
        data.RxPosition = RxPositionComboBox.SelectedIndex == 1 ? 1u : 0u;
        return true;
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".met");
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            metrologySession.LoadFromFile(file.Path);
            // Происхождение файла неизвестно, поэтому признак калибровки
            // снимается: иначе содержимое чужого файла ушло бы в базу
            // как результат калибровки этого прибора.
            calibratedInSession = false;
            FillForm(metrologySession.Current!.Value);
            metrologyUpdated();
            UpdateConnectionControls();
            await ShowMessageAsync("Метрология", "Метрология загружена из файла.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Ошибка чтения", exception.Message);
        }
    }

    private async void SaveFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out MetrologyData data, out string error))
        {
            await ShowMessageAsync("Ошибка данных", error);
            return;
        }

        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = data.Serial > 0 ? $"metrology_{data.Serial}" : "metrology"
        };
        picker.FileTypeChoices.Add("Файл метрологии", new List<string> { ".bin" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            metrologySession.SaveToFile(file.Path, data);
            FillForm(data);
            metrologyUpdated();
            await ShowMessageAsync("Метрология", "Метрология сохранена в файл.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Ошибка сохранения", exception.Message);
        }
    }

    private async void ReadDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        await ReadDeviceAsync(showSuccessMessage: true);
    }

    private async Task<bool> ReadDeviceAsync(bool showSuccessMessage)
    {
        if (!serialDeviceService.IsConnected)
        {
            await ShowMessageAsync("Метрология", "Прибор не подключён.");
            return false;
        }

        SetBusy(true);
        try
        {
            MetrologyReadResult result = await serialDeviceService.RequestMetrologyAsync();
            if (!result.IsSuccess)
            {
                await ShowMessageAsync("Ошибка чтения", SerialDeviceService.ResultToDisplayText(result.Result));
                return false;
            }

            metrologySession.Set(result.Data, MetrologySource.Device);
            FillForm(result.Data);
            metrologyUpdated();
            if (showSuccessMessage)
            {
                await ShowMessageAsync("Метрология", "Метрология считана из зонда.");
            }

            return true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void WriteDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!serialDeviceService.IsConnected)
        {
            await ShowMessageAsync("Метрология", "Прибор не подключён.");
            return;
        }

        if (!TryReadForm(out MetrologyData data, out string error))
        {
            await ShowMessageAsync("Ошибка данных", error);
            return;
        }

        if (!await ConfirmAsync("Запись метрологии", "Записать текущие метрологические данные в зонд?"))
        {
            return;
        }

        SetBusy(true);
        try
        {
            SerialOperationResult result = await serialDeviceService.WriteMetrologyAsync(data);
            if (result != SerialOperationResult.Success)
            {
                await ShowMessageAsync("Ошибка записи", SerialDeviceService.ResultToDisplayText(result));
                return;
            }

            MetrologyData applied = serialDeviceService.CurrentMetrology ?? data;
            metrologySession.Set(applied, MetrologySource.DeviceWrite);
            FillForm(applied);
            metrologyUpdated();
            await ShowMessageAsync("Метрология", "Метрология записана и верифицирована.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AirCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!serialDeviceService.IsConnected)
        {
            await ShowMessageAsync("Автокалибровка", "Прибор не подключён.");
            return;
        }

        if (!await ConfirmAsync(
                "Автокалибровка нулей воздуха",
                "Запустить автокалибровку нулей воздуха? Опрос прибора будет временно приостановлен."))
        {
            return;
        }

        SetBusy(true);
        try
        {
            SerialOperationResult result = await serialDeviceService.RequestAirCalibrationAsync();
            if (result != SerialOperationResult.Success)
            {
                await ShowMessageAsync("Ошибка автокалибровки", SerialDeviceService.ResultToDisplayText(result));
                return;
            }
        }
        finally
        {
            SetBusy(false);
        }

        // Перечитывание не предлагается, а выполняется: новые нули воздуха
        // находятся в приборе, и до перечитывания в окне остаются
        // докалибровочные значения. Признак калибровки поднимается только
        // после успешного чтения — иначе в базу как «Калибровка» ушли бы
        // именно старые значения.
        bool reread = await ReadDeviceAsync(showSuccessMessage: false);
        calibratedInSession = reread;
        UpdateConnectionControls();

        await ShowMessageAsync(
            "Автокалибровка завершена",
            reread
                ? "Нули воздуха пересчитаны, метрология перечитана из зонда."
                : "Нули воздуха пересчитаны, но перечитать метрологию не удалось. " +
                  "Считайте её из зонда вручную — в окне остались значения до калибровки.");
    }

    private void GeometryReadOnlyCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateGeometryEditingState();
    }

    private void UpdateGeometryEditingState()
    {
        bool isReadOnly = GeometryReadOnlyCheckBox.IsChecked == true;
        foreach (TextBox textBox in l1TextBoxes.Concat(l2TextBoxes).Append(Frequency1TextBox).Append(Frequency2TextBox))
        {
            textBox.IsReadOnly = isReadOnly;
        }
    }

    private void SetBusy(bool busy)
    {
        ReadDeviceButton.IsEnabled = !busy && serialDeviceService.IsConnected;
        WriteDeviceButton.IsEnabled = !busy && serialDeviceService.IsConnected;
        AirCalibrationButton.IsEnabled = !busy && serialDeviceService.IsConnected;

        // Выгружать можно и без прибора: метрология могла быть открыта из файла.
        UploadToDatabaseButton.IsEnabled = !busy && metrologySession.HasData;
        UploadToDatabaseButton.Content = calibratedInSession
            ? "Загрузить калибровку в базу"
            : "Загрузить в базу данных";
    }

    /// <summary>
    /// Отправляет 240 байт метрологии в карточку прибора. Раздел выбирается не
    /// пользователем, а состоянием: после автокалибровки это «Калибровка»,
    /// в остальных случаях — «Метрология».
    /// </summary>
    private async void UploadToDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!metrologySession.TryGet(out MetrologyData data))
        {
            await ShowMessageAsync("Загрузка в базу", "Метрология не считана и не открыта из файла.");
            return;
        }

        UploadKind? selectedKind = await ResolveUploadKindAsync();
        if (selectedKind is null)
        {
            return;
        }

        UploadKind kind = selectedKind.Value;
        byte[] content = new byte[BinaryLayoutConstants.MetrologyDataSize];
        MemoryMarshal.Write(content.AsSpan(), in data);

        // Серийный номер для учёта закодирован в сигнатуре — это поле
        // «Серийный №» окна. Поле «Корпус №» (MetrologyData.Serial) содержит
        // номер корпуса и для поиска прибора не используется: номер корпуса
        // карточки приходит из ответа сервера.
        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(data.Signature);
        string description = metrologySession.Source == MetrologySource.File
            ? $"Файл метрологии: {Path.GetFileName(metrologySession.CurrentFilePath)}"
            : calibratedInSession
                ? "Метрология из зонда после автокалибровки нулей воздуха"
                : "Метрология из зонда";

        SetBusy(true);
        try
        {
            await uploadFlow.RunAsync(
                RootGrid.XamlRoot,
                new UploadSource(
                    kind,
                    content,
                    ".bin",
                    identity.Number,
                    identity.DeviceType,
                    identity.TransmitterCount,
                    description));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Определяет раздел карточки прибора, в который уйдёт метрология.
    ///
    /// Для метрологии из зонда раздел известен приложению: «Калибровка» —
    /// только если автокалибровка была выполнена в этой сессии, иначе
    /// «Метрология». Выбор здесь пользователю не предлагается: он ничего не
    /// добавляет, а ошибиться позволяет.
    ///
    /// Для метрологии из файла всё наоборот. Приложение не знает, что именно
    /// открыли — текущие параметры прибора или сохранённый результат
    /// калибровки, — и знает это только пользователь.
    /// </summary>
    private async Task<UploadKind?> ResolveUploadKindAsync()
    {
        if (metrologySession.Source != MetrologySource.File)
        {
            return calibratedInSession ? UploadKind.Calibration : UploadKind.Metrology;
        }

        RadioButton metrologyOption = new()
        {
            Content = UploadKind.Metrology.ToDisplayName(),
            GroupName = "MetrologyUploadKind",
            IsChecked = true
        };
        RadioButton calibrationOption = new()
        {
            Content = UploadKind.Calibration.ToDisplayName(),
            GroupName = "MetrologyUploadKind"
        };

        StackPanel panel = new() { Spacing = 8, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Открыт файл {Path.GetFileName(metrologySession.CurrentFilePath)}. " +
                   "Выберите раздел карточки прибора, в который его загрузить.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(metrologyOption);
        panel.Children.Add(calibrationOption);

        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Тип загружаемой метрологии",
            Content = panel,
            PrimaryButtonText = "Далее",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return calibrationOption.IsChecked == true ? UploadKind.Calibration : UploadKind.Metrology;
    }

    private void UpdateConnectionControls()
    {
        SetBusy(false);
        if (metrologySession.TryGet(out MetrologyData current))
        {
            RefreshFirmwareDependentControls(current, force: false);
        }
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "ОК"
        };
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "Да",
            CloseButtonText = "Нет",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static bool TryReadUShort(
        TextBox textBox,
        string fieldName,
        ushort currentValue,
        out ushort value,
        out string error)
    {
        string text = textBox.Text.Trim();
        if (text.Length == 0)
        {
            value = currentValue;
            error = string.Empty;
            return true;
        }

        if (ushort.TryParse(text, out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"{fieldName} должен быть целым числом от 0 до 65535.";
        return false;
    }

    private static bool TryReadShort(
        TextBox textBox,
        string fieldName,
        short currentValue,
        out short value,
        out string error)
    {
        string text = textBox.Text.Trim();
        if (text.Length == 0)
        {
            value = currentValue;
            error = string.Empty;
            return true;
        }

        if (short.TryParse(text, out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"{fieldName} должен быть целым числом от -32768 до 32767.";
        return false;
    }

    private static bool TryReadFloat(
        TextBox textBox,
        string fieldName,
        float currentValue,
        out float value,
        out string error)
    {
        string text = textBox.Text.Trim();
        if (text.Length == 0)
        {
            value = currentValue;
            error = string.Empty;
            return true;
        }

        // Запятая и точка принимаются одинаково: поле заполняют вручную, и
        // раскладка разделителя не должна стоить пользователю записи в прибор.
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            error = string.Empty;
            return true;
        }

        value = currentValue;
        error = $"{fieldName} должен быть числом.";
        return false;
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

    private async void MetrologyWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        // XAML применяет окончательный размер после Activate. Даём завершиться
        // этому проходу, затем закрепляем клиентскую область и центрирование.
        await Task.Delay(150);
        DispatcherQueue.TryEnqueue(() =>
        {
            IntPtr windowHandle = WindowNative.GetWindowHandle(this);
            ResizeForCurrentScale(windowHandle);
            SecondaryWindowIntegration.CenterOverOwner(windowHandle, currentOwnerHandle);
        });
    }

    private void ResizeForCurrentScale(IntPtr windowHandle)
    {
        uint dpi = windowHandle != IntPtr.Zero ? GetDpiForWindow(windowHandle) : 96u;
        double scale = dpi > 0u ? dpi / 96.0 : 1.0;
        double widthDeficit = RootGrid.ActualWidth > 0.0
            ? Math.Max(0.0, RequiredContentWidth - RootGrid.ActualWidth)
            : 0.0;
        double heightDeficit = RootGrid.ActualHeight > 0.0
            ? Math.Max(0.0, RequiredContentHeight - RootGrid.ActualHeight)
            : 0.0;
        int requestedWidth = Math.Max(
            (int)Math.Ceiling(WindowWidth * scale),
            AppWindow.Size.Width + (int)Math.Ceiling(widthDeficit * scale));
        int requestedHeight = Math.Max(
            (int)Math.Ceiling(WindowHeight * scale),
            AppWindow.Size.Height + (int)Math.Ceiling(heightDeficit * scale));
        AppWindow.Resize(new SizeInt32(requestedWidth, requestedHeight));

        // После Activate WinUI может повторно применить размер от XAML-содержимого.
        // Закрепляем минимум именно по физической клиентской области, включая DPI.
        if (!GetWindowRect(windowHandle, out Rect windowRect)
            || !GetClientRect(windowHandle, out Rect clientRect))
        {
            return;
        }

        int clientWidth = clientRect.Right - clientRect.Left;
        int clientHeight = clientRect.Bottom - clientRect.Top;
        int requiredClientWidth = (int)Math.Ceiling(RequiredContentWidth * scale);
        int requiredClientHeight = (int)Math.Ceiling(RequiredContentHeight * scale);
        int outerWidth = windowRect.Right - windowRect.Left;
        int outerHeight = windowRect.Bottom - windowRect.Top;
        int correctedWidth = Math.Max(requestedWidth, outerWidth + Math.Max(0, requiredClientWidth - clientWidth));
        int correctedHeight = Math.Max(requestedHeight, outerHeight + Math.Max(0, requiredClientHeight - clientHeight));
        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            correctedWidth,
            correctedHeight,
            NoMove | NoZOrder);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
