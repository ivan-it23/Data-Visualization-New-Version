using AMK.DataVisualization.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

/// <summary>
/// Lets the user choose the original application scale or one adjacent larger or
/// smaller preset.
/// </summary>
public sealed partial class WindowScaleWindow : Window
{
    private readonly WindowScaleService scaleService;
    private bool allowPermanentClose;
    private bool activated;
    private bool updatingOptions;

    internal WindowScaleWindow(WindowScaleService scaleService)
    {
        this.scaleService = scaleService;
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(400, 300));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = false;
        }

        AppWindow.Closing += AppWindow_Closing;
        BuildOptions();
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        SecondaryWindowIntegration.ConfigureForOwner(windowHandle, ownerHandle);

        RefreshOptions();

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
    }

    public void ClosePermanently()
    {
        allowPermanentClose = true;
        Close();
    }

    private void BuildOptions()
    {
        foreach (double scale in WindowScaleService.ManualScales)
        {
            OptionsPanel.Children.Add(CreateOption(FormatScale(scale), scale));
        }

        RefreshOptions();
    }

    private RadioButton CreateOption(string label, double scale)
    {
        RadioButton option = new()
        {
            Content = label,
            Tag = scale,
            GroupName = "WindowScale"
        };
        option.Checked += Option_Checked;
        return option;
    }

    private void Option_Checked(object sender, RoutedEventArgs e)
    {
        if (updatingOptions || sender is not RadioButton { Tag: double scale })
        {
            return;
        }

        scaleService.SetManualScale(scale);

        RefreshOptions();
    }

    private void RefreshOptions()
    {
        updatingOptions = true;
        try
        {
            foreach (RadioButton option in OptionsPanel.Children.OfType<RadioButton>())
            {
                if (option.Tag is double scale)
                {
                    option.IsChecked = Math.Abs(scale - scaleService.ManualScale) < 0.001;
                }
            }
        }
        finally
        {
            updatingOptions = false;
        }
    }

    private static string FormatScale(double scale)
    {
        return $"{Math.Round(scale * 100)}%";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
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
}
