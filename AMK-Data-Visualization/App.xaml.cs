using Microsoft.UI.Xaml;

namespace AMK.DataVisualization.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WriteLifecycle("before-main-window");
            window = new MainWindow();
            WriteLifecycle($"after-main-window hwnd={WinRT.Interop.WindowNative.GetWindowHandle(window)}");
            window.Activated += (_, eventArgs) => WriteLifecycle($"activated state={eventArgs.WindowActivationState}");
            window.VisibilityChanged += (_, eventArgs) => WriteLifecycle($"visibility visible={eventArgs.Visible}");
            window.Closed += (_, _) => WriteLifecycle("closed");
            window.Activate();
            WriteLifecycle($"after-activate hwnd={WinRT.Interop.WindowNative.GetWindowHandle(window)}");
        }
        catch (Exception exception)
        {
            WriteDiagnostic("launch", exception);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteDiagnostic("unhandled", e.Exception);
    }

    private static void WriteDiagnostic(string stage, Exception exception)
    {
        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "amk-winui-error.txt"),
            $"{DateTimeOffset.Now:O} [{stage}]{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private static void WriteLifecycle(string message)
    {
        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "amk-winui-lifecycle.txt"),
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
