using System.Runtime.InteropServices;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.IO.DataFiles;
using AMK.DataVisualization.Metrology;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

public sealed partial class SchematicWindow : Window
{
    private const int WindowWidth = 820;

    // Окно нерастягиваемое, поэтому его высота — это сумма рядов правой
    // колонки, и добавление ряда требует пересчёта. Ряды: заголовок «Быстрые
    // пресеты» (~20), сетка пресетов (2 кнопки по 32 + зазор), блоки «УЭС» и
    // «Симм. фазы» (~113), блоки «УЭС по затуханию» и «Затухание» (~113),
    // кнопки молчащих каналов (32), четыре зазора по 6. Ниже — ряд «Применить»
    // и «Отмена» (32), над колонкой — схема прибора и выбор частоты с осью X.
    //
    // Значение с запасом: при 569 обрезался весь ряд амплитудных величин, при
    // 675 не помещался последний ряд кнопок.
    //
    // Верхний предел задаёт масштаб окон: WindowScaleService увеличивает
    // базовый размер до 115 %, то есть до 943x828 физических пикселей.
    private const int WindowHeight = 720;
    private const int WindowOwnerIndex = -8;
    private const int WindowExtendedStyleIndex = -20;
    private const int ClassIconIndex = -14;
    private const int ClassSmallIconIndex = -34;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int WindowMessageGetIcon = 0x007F;
    private const int WindowMessageSetIcon = 0x0080;
    private const long ExtendedStyleAppWindow = 0x00040000L;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const int ShowRestore = 9;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private readonly ChartWorkspace chartWorkspace;
    private readonly CurveSelectionService curveSelectionService;
    private readonly MetrologySession metrologySession;
    private readonly Action<int> configurationApplied;
    private readonly Dictionary<int, EditorSession> editorSessions = [];
    private SchematicConfigurationEditor? editor;
    private int targetGraphNumber = 1;
    private bool controlsAreUpdating;
    private bool allowPermanentClose;
    private bool activated;

    internal SchematicWindow(
        ChartWorkspace chartWorkspace,
        CurveSelectionService curveSelectionService,
        MetrologySession metrologySession,
        Action<int> configurationApplied)
    {
        this.chartWorkspace = chartWorkspace;
        this.curveSelectionService = curveSelectionService;
        this.metrologySession = metrologySession;
        this.configurationApplied = configurationApplied;
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }

        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowForGraph(int graphNumber, IntPtr ownerHandle)
    {
        if (graphNumber is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(graphNumber));
        }

        targetGraphNumber = graphNumber;
        Title = $"Настройка кривых — График {targetGraphNumber}";
        ActivateEditorForGraph(graphNumber);

        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        if (ownerHandle != IntPtr.Zero)
        {
            SetWindowLongPtr(windowHandle, WindowOwnerIndex, ownerHandle);
        }

        EnsureTaskbarPresenceAndIcon(windowHandle, ownerHandle);

        if (!activated)
        {
            Activate();
            activated = true;
        }
        else
        {
            AppWindow.Show();
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowRestore);
        }

        CenterOverOwner(windowHandle, ownerHandle);
    }

    public void RefreshDataContext(params int[] graphNumbers)
    {
        foreach (int graphNumber in graphNumbers.Distinct())
        {
            if (graphNumber is 1 or 2)
            {
                editorSessions.Remove(graphNumber);
            }
        }

        if (editor is not null && graphNumbers.Contains(targetGraphNumber))
        {
            ActivateEditorForGraph(targetGraphNumber);
        }
    }

    public void RefreshMetrologyAvailability()
    {
        if (editor is not null)
        {
            RefreshControls();
        }
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        Close();
    }

    private void ActivateEditorForGraph(int graphNumber)
    {
        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(graphNumber);
        if (editorSessions.TryGetValue(graphNumber, out EditorSession? existingSession)
            && ReferenceEquals(existingSession.Repository, repository)
            && existingSession.DataVersion == repository.DataVersion)
        {
            editor = existingSession.Editor;
            RefreshControls();
            return;
        }

        // Only a configuration this window produced may be re-derived into a
        // schematic selection; one built on the chart opens without highlighting.
        editor = new SchematicConfigurationEditor(
            curveSelectionService,
            chartWorkspace.GetAvailableTransmitterCountForGraph(graphNumber),
            chartWorkspace.GetConfigurationForGraph(graphNumber),
            chartWorkspace.GetConfigurationSourceForGraph(graphNumber) == ChartConfigurationSource.CurveSetupWindow);
        editorSessions[graphNumber] = new EditorSession(editor, repository, repository.DataVersion);
        RefreshControls();
    }

    private void RefreshControls()
    {
        if (editor is null)
        {
            return;
        }

        controlsAreUpdating = true;
        FrequencyComboBox.SelectedIndex = editor.Selection.Frequency == FrequencyBand.Freq400KHz ? 0 : 1;
        XAxisTypeComboBox.SelectedIndex = editor.XAxisType == XAxisType.FrameNumber ? 0 : 1;

        RefreshElementButtons();
        RefreshSmtCheckBoxes();
        RefreshCurveList();

        ApplyButton.IsEnabled = editor.ConfigurationError.Length == 0;
        Title = $"Настройка кривых — График {targetGraphNumber}";
        controlsAreUpdating = false;
    }

    private void RefreshElementButtons()
    {
        if (editor is null)
        {
            return;
        }

        SetElementState(Transmitter1Button, ElementType.Transmitter, 0);
        SetElementState(Transmitter2Button, ElementType.Transmitter, 1);
        SetElementState(Transmitter3Button, ElementType.Transmitter, 2);
        SetElementState(Transmitter4Button, ElementType.Transmitter, 3);
        SetElementState(Transmitter5Button, ElementType.Transmitter, 4);
        SetElementState(Receiver1Button, ElementType.Receiver, 0);
        SetElementState(Receiver2Button, ElementType.Receiver, 1);
        SetElementState(SilentReceiver1Button, ElementType.SilentTx, 0);
        SetElementState(SilentReceiver2Button, ElementType.SilentTx, 1);
    }

    private void SetElementState(ToggleButton button, ElementType type, int index)
    {
        if (editor is null)
        {
            return;
        }

        bool enabled = type != ElementType.Transmitter || editor.Selection.SupportsTransmitter(index);
        bool selected = editor.Selection.DataFilter == DataTypeFilter.All && type switch
        {
            ElementType.Transmitter when enabled => editor.Selection.Transmitters[index],
            ElementType.Receiver => editor.Selection.Receivers[index],
            ElementType.SilentTx => editor.Selection.SilentTransmitters[index],
            _ => false
        };

        button.IsEnabled = enabled;
        button.IsChecked = selected;
        button.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 255, 214, 0) : Colors.Transparent);
        button.BorderThickness = new Thickness(3);
        button.Background = new SolidColorBrush(type switch
        {
            ElementType.Transmitter => Color.FromArgb(255, 190, 105, 105),
            ElementType.Receiver => Color.FromArgb(255, 105, 185, 105),
            ElementType.SilentTx => Color.FromArgb(255, 145, 145, 145),
            _ => Colors.Gray
        });
        button.Foreground = new SolidColorBrush(Colors.White);
    }

    private void RefreshSmtCheckBoxes()
    {
        if (editor is null)
        {
            return;
        }

        bool rhoActive = editor.Selection.DataFilter == DataTypeFilter.RhoSmtOnly;
        RhoFromSondeCheckBox.IsChecked = rhoActive && editor.Selection.RhoSmtMode == SmtComputationMode.FromSonde;
        RhoFromComputerCheckBox.IsChecked = rhoActive && editor.Selection.RhoSmtMode == SmtComputationMode.FromComputer;
        RhoFromComputerCheckBox.IsEnabled = metrologySession.HasData;

        bool phaseActive = editor.Selection.DataFilter == DataTypeFilter.PhaseSmtOnly;
        PhaseFromSondeCheckBox.IsChecked = phaseActive && editor.Selection.PhaseSmtMode == SmtComputationMode.FromSonde;
        PhaseFromComputerCheckBox.IsChecked = phaseActive && editor.Selection.PhaseSmtMode == SmtComputationMode.FromComputer;
        PhaseFromComputerCheckBox.IsEnabled = metrologySession.HasData;

        // Амплитудные виды данных отдаёт только прибор новой прошивки. Панель
        // остаётся на месте, чтобы состав окна не менялся под руками, но её
        // флажки недоступны и она приглушена.
        bool extendedFirmware = chartWorkspace.IsExtendedFirmwareSourceForGraph(targetGraphNumber);
        ExtendedSmtPanel.Opacity = extendedFirmware ? 1.0 : 0.5;

        bool rhoAttActive = extendedFirmware && editor.Selection.DataFilter == DataTypeFilter.RhoAttSmtOnly;
        RhoAttFromSondeCheckBox.IsChecked = rhoAttActive && editor.Selection.RhoAttSmtMode == SmtComputationMode.FromSonde;
        RhoAttFromComputerCheckBox.IsChecked = rhoAttActive && editor.Selection.RhoAttSmtMode == SmtComputationMode.FromComputer;
        RhoAttFromSondeCheckBox.IsEnabled = extendedFirmware;
        RhoAttFromComputerCheckBox.IsEnabled = extendedFirmware && metrologySession.HasData;

        bool attDbActive = extendedFirmware && editor.Selection.DataFilter == DataTypeFilter.AttDbSmtOnly;
        AttDbFromSondeCheckBox.IsChecked = attDbActive && editor.Selection.AttDbSmtMode == SmtComputationMode.FromSonde;
        AttDbFromComputerCheckBox.IsChecked = attDbActive && editor.Selection.AttDbSmtMode == SmtComputationMode.FromComputer;
        AttDbFromSondeCheckBox.IsEnabled = extendedFirmware;
        AttDbFromComputerCheckBox.IsEnabled = extendedFirmware && metrologySession.HasData;
    }

    private void RefreshCurveList()
    {
        if (editor is null)
        {
            return;
        }

        CurveItemsPanel.Children.Clear();
        if (!editor.Selection.IsValid)
        {
            AddCurveMessage(editor.Selection.ValidationError);
            return;
        }

        if (editor.AvailableCurves.Count == 0)
        {
            AddCurveMessage("Нет доступных кривых для текущего выбора");
            return;
        }

        if (ShouldUseFlatCurveList())
        {
            foreach (CurveDefinition curve in editor.AvailableCurves)
            {
                AddCurveCheckBox(curve);
            }
            return;
        }

        foreach (IGrouping<(ElementType Type, int Index), CurveDefinition> group in editor.AvailableCurves.GroupBy(curve => (curve.SourceElement, curve.ElementIndex)))
        {
            CurveItemsPanel.Children.Add(new TextBlock
            {
                Text = GetElementGroupName(group.Key.Type, group.Key.Index),
                Margin = new Thickness(4, 6, 0, 1),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.82
            });
            foreach (CurveDefinition curve in group)
            {
                AddCurveCheckBox(curve);
            }
        }
    }

    private bool ShouldUseFlatCurveList()
    {
        if (editor is null)
        {
            return true;
        }

        bool forceFlat = editor.Selection.DataFilter is DataTypeFilter.RhoSmtOnly or DataTypeFilter.PhaseSmtOnly
            || (editor.Selection.DataFilter == DataTypeFilter.AmplitudeOnly
                && editor.Selection.SelectedReceiverCount == 0
                && editor.Selection.SelectedTransmitterCount == 0
                && editor.Selection.SelectedSilentCount > 0);
        int distinctElements = editor.AvailableCurves.Select(curve => (curve.SourceElement, curve.ElementIndex)).Distinct().Count();
        return editor.Selection.SelectedReceiverCount is 1 or 2 || forceFlat || distinctElements == 1;
    }

    private void AddCurveCheckBox(CurveDefinition curve)
    {
        string axisSuffix = editor?.CurrentPreset is PresetType.None or PresetType.Custom
            ? $" [{(curve.YAxisGroup == 0 ? "Левая ось" : "Правая ось")}]"
            : string.Empty;
        CheckBox checkBox = new()
        {
            Content = $"{curve.DisplayName} ({curve.Units}){axisSuffix}",
            IsChecked = curve.Enabled,
            MinWidth = 0,
            Tag = curve.Id
        };
        checkBox.Click += CurveCheckBox_Click;
        CurveItemsPanel.Children.Add(checkBox);
    }

    private void AddCurveMessage(string message)
    {
        CurveItemsPanel.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(6),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76
        });
    }

    private void SchematicElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (controlsAreUpdating || editor is null || sender is not ToggleButton button || button.Tag is not string tag)
        {
            return;
        }

        string[] parts = tag.Split(':');
        if (parts.Length == 2
            && Enum.TryParse(parts[0], out ElementType type)
            && int.TryParse(parts[1], out int index))
        {
            editor.ToggleElement(type, index);
            RefreshControls();
        }
    }

    private void FrequencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (controlsAreUpdating || editor is null || FrequencyComboBox.SelectedIndex < 0)
        {
            return;
        }

        editor.SetFrequency(FrequencyComboBox.SelectedIndex == 0 ? FrequencyBand.Freq400KHz : FrequencyBand.Freq2000KHz);
        RefreshControls();
    }

    private void XAxisTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (controlsAreUpdating || editor is null || XAxisTypeComboBox.SelectedIndex < 0)
        {
            return;
        }

        editor.XAxisType = XAxisTypeComboBox.SelectedIndex == 0 ? XAxisType.FrameNumber : XAxisType.Temperature;
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (editor is null || sender is not Button button || button.Tag is not string tag || !Enum.TryParse(tag, out PresetType preset))
        {
            return;
        }

        editor.ApplyPreset(preset);
        RefreshControls();
    }

    private async void SmtModeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (controlsAreUpdating || editor is null || sender is not CheckBox checkBox || checkBox.Tag is not string tag)
        {
            return;
        }

        string[] parts = tag.Split(':');
        if (parts.Length != 2
            || !Enum.TryParse(parts[0], out PresetType preset)
            || !Enum.TryParse(parts[1], out SmtComputationMode mode))
        {
            return;
        }

        bool accepted = editor.TrySetSmtMode(preset, mode, metrologySession.HasData, out string warning);
        RefreshControls();
        if (!accepted && warning.Length > 0)
        {
            await ShowMessageAsync("Метрология недоступна", warning);
        }
    }

    private void CurveCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (editor is null || sender is not CheckBox checkBox || checkBox.Tag is not string curveId)
        {
            return;
        }

        editor.SetCurveEnabled(curveId, checkBox.IsChecked == true);
        ApplyButton.IsEnabled = editor.ConfigurationError.Length == 0;
        Title = $"Настройка кривых — График {targetGraphNumber}";
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (editor is null || !editor.CanApply)
        {
            return;
        }

        ToolDataRepository repository = chartWorkspace.GetRepositoryForGraph(targetGraphNumber);
        if (!repository.HasData)
        {
            await ShowMessageAsync(
                "Нет данных для отображения",
                "Невозможно вывести кривые на график: данные отсутствуют.\nОткройте файл данных или подключите прибор к COM-порту.");
            return;
        }

        ChartConfiguration configuration = editor.BuildConfiguration();
        if (!curveSelectionService.ValidateConfiguration(configuration))
        {
            await ShowMessageAsync("Ошибка", "Error applying curve configuration");
            return;
        }

        chartWorkspace.ApplyConfigurationForGraph(
            targetGraphNumber,
            configuration,
            ChartConfigurationSource.CurveSetupWindow);
        configurationApplied(targetGraphNumber);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
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

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowPermanentClose)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private static string GetElementGroupName(ElementType type, int index)
    {
        return type switch
        {
            ElementType.Transmitter => $"T{index + 1}",
            ElementType.Receiver => $"R{index + 1}",
            ElementType.SilentTx => $"Молч. R{index + 1}",
            _ => $"Группа {index + 1}"
        };
    }

    private static void CenterOverOwner(IntPtr windowHandle, IntPtr ownerHandle)
    {
        if (windowHandle == IntPtr.Zero || ownerHandle == IntPtr.Zero || !GetWindowRect(ownerHandle, out Rect ownerRect))
        {
            return;
        }

        int x = ownerRect.Left + Math.Max(0, (ownerRect.Right - ownerRect.Left - WindowWidth) / 2);
        int y = ownerRect.Top + Math.Max(0, (ownerRect.Bottom - ownerRect.Top - WindowHeight) / 2);
        SetWindowPos(windowHandle, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder);
    }

    private static void EnsureTaskbarPresenceAndIcon(IntPtr windowHandle, IntPtr ownerHandle)
    {
        long extendedStyle = GetWindowLongPtr(windowHandle, WindowExtendedStyleIndex).ToInt64();
        extendedStyle |= ExtendedStyleAppWindow;
        extendedStyle &= ~ExtendedStyleToolWindow;
        SetWindowLongPtr(windowHandle, WindowExtendedStyleIndex, new IntPtr(extendedStyle));

        if (ownerHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr largeIcon = SendMessage(ownerHandle, WindowMessageGetIcon, new IntPtr(IconBig), IntPtr.Zero);
        if (largeIcon == IntPtr.Zero)
        {
            largeIcon = GetClassLongPtr(ownerHandle, ClassIconIndex);
        }

        IntPtr smallIcon = SendMessage(ownerHandle, WindowMessageGetIcon, new IntPtr(IconSmall), IntPtr.Zero);
        if (smallIcon == IntPtr.Zero)
        {
            smallIcon = GetClassLongPtr(ownerHandle, ClassSmallIconIndex);
        }

        if (largeIcon != IntPtr.Zero)
        {
            SendMessage(windowHandle, WindowMessageSetIcon, new IntPtr(IconBig), largeIcon);
        }

        if (smallIcon != IntPtr.Zero)
        {
            SendMessage(windowHandle, WindowMessageSetIcon, new IntPtr(IconSmall), smallIcon);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record EditorSession(
        SchematicConfigurationEditor Editor,
        ToolDataRepository Repository,
        ulong DataVersion);
}
