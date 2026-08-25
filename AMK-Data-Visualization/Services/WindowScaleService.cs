using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Application-wide zoom of the whole window content. The original application
/// layout is 100%; the user can choose one smaller or one larger preset.
///
/// Known limitation: flyouts, menus and content dialogs are hosted in the popup
/// root rather than inside the window content, so they keep their system size and
/// do not follow this zoom.
/// </summary>
internal sealed class WindowScaleService
{
    public const double DefaultScale = 1.0;

    // Пределы подобраны по тому, как окна ведут себя на деле: при 125 % часть
    // элементов не помещалась на экран, при 75 % становилась слишком мелкой.
    // Сохранённый ранее выбор не теряется: NormalizeScaleChoice приводит его к
    // ближайшему доступному, то есть 125 % становится 115 %, а 75 % — 85 %.
    public const double MinimumScale = 0.85;
    public const double MaximumScale = 1.15;

    private const double ScaleEpsilon = 1e-9;

    /// <summary>Larger, default and smaller values in their display order.</summary>
    public static IReadOnlyList<double> ManualScales { get; } = [MaximumScale, DefaultScale, MinimumScale];

    private readonly Dictionary<Window, ScaledWindow> attachedWindows = [];

    public event EventHandler? ScaleChanged;

    public WindowScaleSettings Settings { get; private set; } = new();

    public double CurrentScale { get; private set; } = DefaultScale;

    public double ManualScale => Settings.ManualScale;

    public void Initialize(WindowScaleSettings storedSettings)
    {
        Settings = storedSettings;
        CurrentScale = NormalizeScaleChoice(storedSettings.ManualScale);
        Settings.ManualScale = CurrentScale;
    }

    /// <summary>
    /// Wraps the window content in a host panel and starts scaling it. Windows with
    /// a fixed size pass <paramref name="resizeWithScale"/> so the frame grows
    /// together with its content; the maximized main window does not.
    /// </summary>
    public void Attach(Window window, bool resizeWithScale)
    {
        if (attachedWindows.ContainsKey(window) || window.Content is not FrameworkElement content)
        {
            return;
        }

        SizeInt32 baseSize = window.AppWindow.Size;
        window.Content = null;

        Grid host = new();
        content.RenderTransformOrigin = new Point(0, 0);
        host.Children.Add(content);
        window.Content = host;

        ScaledWindow entry = new(host, content, resizeWithScale, baseSize);
        attachedWindows[window] = entry;

        host.SizeChanged += (_, args) => ApplyLayoutSize(entry, args.NewSize);
        window.Closed += (_, _) => attachedWindows.Remove(window);

        ApplyTransform(entry);
        ApplyWindowSize(window, entry);
    }

    public void SetManualScale(double scale)
    {
        Settings.ManualScale = NormalizeScaleChoice(scale);
        UpdateScale();
    }

    private void UpdateScale()
    {
        CurrentScale = NormalizeScaleChoice(Settings.ManualScale);

        // Snapshot: resizing a window can let its Closed handler run, and that
        // handler removes the entry from the dictionary being walked.
        foreach ((Window window, ScaledWindow entry) in attachedWindows.ToList())
        {
            ApplyTransform(entry);
            ApplyWindowSize(window, entry);
        }

        ScaleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTransform(ScaledWindow entry)
    {
        entry.Content.RenderTransform = IsUnscaled
            ? null
            : new ScaleTransform { ScaleX = CurrentScale, ScaleY = CurrentScale };
        ApplyLayoutSize(entry, new Size(entry.Host.ActualWidth, entry.Host.ActualHeight));
    }

    /// <summary>
    /// Gives the content the size it would have had on the reference display. The
    /// host reports its own measured size, so no conversion between DIPs and
    /// physical pixels is involved.
    /// </summary>
    private void ApplyLayoutSize(ScaledWindow entry, Size hostSize)
    {
        if (IsUnscaled)
        {
            entry.Content.ClearValue(FrameworkElement.WidthProperty);
            entry.Content.ClearValue(FrameworkElement.HeightProperty);
            entry.Content.HorizontalAlignment = HorizontalAlignment.Stretch;
            entry.Content.VerticalAlignment = VerticalAlignment.Stretch;
            return;
        }

        if (hostSize.Width <= 0.0 || hostSize.Height <= 0.0)
        {
            // Attach runs before the first layout pass, when the host has no size
            // yet. Sizing the content to zero here would collapse it until the host
            // reports its real size, so the transform alone is applied for now.
            return;
        }

        // Anchored top-left: the transform grows the content down and to the right,
        // so a stretched alignment would fight the explicit size.
        entry.Content.HorizontalAlignment = HorizontalAlignment.Left;
        entry.Content.VerticalAlignment = VerticalAlignment.Top;
        entry.Content.Width = Math.Max(0.0, hostSize.Width / CurrentScale);
        entry.Content.Height = Math.Max(0.0, hostSize.Height / CurrentScale);
    }

    private void ApplyWindowSize(Window window, ScaledWindow entry)
    {
        if (!entry.ResizeWithScale || entry.BaseSize.Width <= 0 || entry.BaseSize.Height <= 0)
        {
            return;
        }

        window.AppWindow.Resize(new SizeInt32(
            (int)Math.Round(entry.BaseSize.Width * CurrentScale),
            (int)Math.Round(entry.BaseSize.Height * CurrentScale)));
    }

    private bool IsUnscaled => Math.Abs(CurrentScale - DefaultScale) <= ScaleEpsilon;

    private static double NormalizeScaleChoice(double scale)
    {
        if (!double.IsFinite(scale))
        {
            return DefaultScale;
        }

        return ManualScales.MinBy(candidate => Math.Abs(candidate - scale));
    }

    private sealed record ScaledWindow(
        Grid Host,
        FrameworkElement Content,
        bool ResizeWithScale,
        SizeInt32 BaseSize);

}
