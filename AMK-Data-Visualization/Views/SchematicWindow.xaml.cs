using System.Runtime.InteropServices;
using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.IO.DataFiles;
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
    private const int WindowHeight = 540;
    private const int WindowOwnerIndex = -8;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private readonly ChartWorkspace chartWorkspace;
    private readonly CurveSelectionService curveSelectionService;
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
        Action<int> configurationApplied)
    {
        this.chartWorkspace = chartWorkspace;
        this.curveSelectionService = curveSelectionService;
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
        Title = $"Схема прибора - График {targetGraphNumber}";
        ActivateEditorForGraph(graphNumber);

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

        int transmitterCount = SchematicSelection.UiTransmitterCount;
        if (repository.TryGetRecordMetadata(0, out ToolRecordMetadata metadata) && metadata.Capabilities.TransmitterCount > 0)
        {
            transmitterCount = metadata.Capabilities.TransmitterCount;
        }

        editor = new SchematicConfigurationEditor(
            curveSelectionService,
            transmitterCount,
            chartWorkspace.GetConfigurationForGraph(graphNumber));
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

        string error = editor.ConfigurationError;
        ApplyButton.IsEnabled = error.Length == 0;
        ValidationTextBlock.Text = TranslateConfigurationError(error);
        Title = $"Схема прибора - График {targetGraphNumber}";
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
        button.BorderBrush = new SolidColorBrush(selected ? Colors.Gold : Colors.DimGray);
        button.BorderThickness = selected ? new Thickness(3) : new Thickness(1);
        button.Background = new SolidColorBrush(type switch
        {
            ElementType.Transmitter => selected ? Color.FromArgb(255, 230, 130, 130) : Color.FromArgb(255, 190, 105, 105),
            ElementType.Receiver => selected ? Color.FromArgb(255, 130, 220, 130) : Color.FromArgb(255, 105, 185, 105),
            ElementType.SilentTx => selected ? Color.FromArgb(255, 180, 180, 180) : Color.FromArgb(255, 145, 145, 145),
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

        bool phaseActive = editor.Selection.DataFilter == DataTypeFilter.PhaseSmtOnly;
        PhaseFromSondeCheckBox.IsChecked = phaseActive && editor.Selection.PhaseSmtMode == SmtComputationMode.FromSonde;
        PhaseFromComputerCheckBox.IsChecked = phaseActive && editor.Selection.PhaseSmtMode == SmtComputationMode.FromComputer;
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
        CheckBox checkBox = new()
        {
            Content = $"{curve.DisplayName} ({curve.Units}) [{(curve.YAxisGroup == 0 ? "Левая ось" : "Правая ось")}]",
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

        bool accepted = editor.TrySetSmtMode(preset, mode, hasMetrologyData: false, out string warning);
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
        string error = editor.ConfigurationError;
        ApplyButton.IsEnabled = error.Length == 0;
        ValidationTextBlock.Text = TranslateConfigurationError(error);
        Title = $"Схема прибора - График {targetGraphNumber}";
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

        chartWorkspace.ApplyConfigurationForGraph(targetGraphNumber, configuration);
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

    private static string TranslateConfigurationError(string error)
    {
        return error switch
        {
            "No curves selected for display" => "Не выбраны кривые для отображения",
            "All curves are disabled" => "Все кривые отключены",
            "Dual axis requires curves on both axes" => "Для двух осей нужны кривые на обеих осях",
            "Too many curves for display (maximum 64)" => "Слишком много кривых (максимум 64)",
            _ => error
        };
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

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

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
