using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Charting.Series;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AMK.DataVisualization.App.Controls;

/// <summary>
/// Compact, on-chart version of the device schematic element row. It mirrors the
/// element buttons of the full schematic window (T1–T5, R1/R2, silent Tx) but at
/// a reduced size so it fits inside the top border of a graph. An inline
/// "Применить" button appears next to "Молч. R2" once the user activates an
/// element, matching the requested behaviour.
/// </summary>
public sealed class InlineSchematicBar : UserControl
{
    private const double ElementWidth = 30;
    private const double SilentWidth = 58;
    private const double ElementHeight = 24;

    private static readonly Color TransmitterColor = Color.FromArgb(255, 190, 105, 105);
    private static readonly Color ReceiverColor = Color.FromArgb(255, 105, 185, 105);
    private static readonly Color SilentColor = Color.FromArgb(255, 145, 145, 145);
    private static readonly Color SelectedBorderColor = Color.FromArgb(255, 255, 214, 0);

    private readonly List<ElementButton> elementButtons = [];
    private readonly Button applyButton;

    public InlineSchematicBar()
    {
        StackPanel layout = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border elementsBorder = new()
        {
            Padding = new Thickness(3),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"]
        };
        StackPanel elementsRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddElement(elementsRow, "T5", ElementType.Transmitter, 4);
        AddElement(elementsRow, "T3", ElementType.Transmitter, 2);
        AddElement(elementsRow, "T1", ElementType.Transmitter, 0);
        AddElement(elementsRow, "R1", ElementType.Receiver, 0);
        AddElement(elementsRow, "R2", ElementType.Receiver, 1);
        AddElement(elementsRow, "T2", ElementType.Transmitter, 1);
        AddElement(elementsRow, "T4", ElementType.Transmitter, 3);
        elementsBorder.Child = elementsRow;
        layout.Children.Add(elementsBorder);

        StackPanel silentRow = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        AddElement(silentRow, "Молч. R1", ElementType.SilentTx, 0, SilentWidth);
        AddElement(silentRow, "Молч. R2", ElementType.SilentTx, 1, SilentWidth);
        layout.Children.Add(silentRow);

        applyButton = new Button
        {
            Content = "Применить",
            Height = ElementHeight,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        applyButton.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        layout.Children.Add(applyButton);

        Content = layout;
    }

    public event EventHandler<SchematicElementToggle>? ElementToggled;

    public event EventHandler? ApplyRequested;

    public void UpdateState(SchematicSelection selection, bool showApply, bool applyEnabled, bool showSelection)
    {
        foreach (ElementButton element in elementButtons)
        {
            bool enabled = element.Type != ElementType.Transmitter || selection.SupportsTransmitter(element.Index);
            bool selected = showSelection && selection.DataFilter == DataTypeFilter.All && element.Type switch
            {
                ElementType.Transmitter when enabled => selection.Transmitters[element.Index],
                ElementType.Receiver => selection.Receivers[element.Index],
                ElementType.SilentTx => selection.SilentTransmitters[element.Index],
                _ => false
            };

            element.Button.IsEnabled = enabled;
            element.Button.BorderBrush = new SolidColorBrush(selected ? SelectedBorderColor : Colors.Transparent);
        }

        applyButton.Visibility = showApply ? Visibility.Visible : Visibility.Collapsed;
        applyButton.IsEnabled = applyEnabled;
    }

    private void AddElement(Panel host, string label, ElementType type, int index, double width = ElementWidth)
    {
        Color background = type switch
        {
            ElementType.Transmitter => TransmitterColor,
            ElementType.Receiver => ReceiverColor,
            _ => SilentColor
        };
        Button button = new()
        {
            Content = label,
            Width = width,
            Height = ElementHeight,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(background),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, $"{label} — нажмите для выбора");
        button.Click += (_, _) => ElementToggled?.Invoke(this, new SchematicElementToggle(type, index));
        elementButtons.Add(new ElementButton(button, type, index));
        host.Children.Add(button);
    }

    private sealed record ElementButton(Button Button, ElementType Type, int Index);
}

public readonly record struct SchematicElementToggle(ElementType Type, int Index);
