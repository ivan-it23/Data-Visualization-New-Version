using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Application-wide zoom of the whole window content. A manual 100% reproduces
/// the physical UI size of the 125%-DPI reference display, independently for
/// every window and monitor. The user can choose one smaller or larger preset.
///
/// Known limitation: flyouts, menus and content dialogs are hosted in the popup
/// root rather than inside the window content, so they keep their system size and
/// do not follow this zoom.
/// </summary>
internal sealed class WindowScaleService
{
    public const double DefaultScale = 1.0;
    public const double ReferenceRasterizationScale = 1.25;

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
    /// together with its content; the maximized main window does not. A window
    /// whose initial frame was already converted from DIPs also passes
    /// <paramref name="baseSizeTracksSystemDpi"/> so its reference frame is not
    /// scaled twice.
    /// </summary>
    public void Attach(
        Window window,
        bool resizeWithScale,
        bool baseSizeTracksSystemDpi = false)
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

        ScaledWindow entry = new(
            window,
            host,
            content,
            resizeWithScale,
            baseSize,
            baseSizeTracksSystemDpi);
        attachedWindows[window] = entry;

        host.Loaded += Host_Loaded;
        host.SizeChanged += Host_SizeChanged;
        window.Closed += Window_Closed;

        ApplyEntry(entry);
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
            ApplyEntry(entry);
        }

        ScaleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Host_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Grid host || FindByHost(host) is not { } entry)
        {
            return;
        }

        BindXamlRoot(entry);
        ApplyEntry(entry);
    }

    private void Host_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is Grid host && FindByHost(host) is { } entry)
        {
            ApplyLayoutSize(entry, args.NewSize);
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        foreach (ScaledWindow entry in attachedWindows.Values
                     .Where(candidate => ReferenceEquals(candidate.XamlRoot, sender))
                     .ToList())
        {
            ApplyEntry(entry);
        }
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (sender is not Window window ||
            !attachedWindows.Remove(window, out ScaledWindow? entry))
        {
            return;
        }

        entry.Host.Loaded -= Host_Loaded;
        entry.Host.SizeChanged -= Host_SizeChanged;
        window.Closed -= Window_Closed;

        if (entry.XamlRoot is not null)
        {
            entry.XamlRoot.Changed -= XamlRoot_Changed;
        }
    }

    private void BindXamlRoot(ScaledWindow entry)
    {
        XamlRoot? xamlRoot = entry.Host.XamlRoot;
        if (xamlRoot is null)
        {
            return;
        }

        if (!ReferenceEquals(entry.XamlRoot, xamlRoot))
        {
            if (entry.XamlRoot is not null)
            {
                entry.XamlRoot.Changed -= XamlRoot_Changed;
            }

            entry.XamlRoot = xamlRoot;
            xamlRoot.Changed += XamlRoot_Changed;
        }

        if (!entry.ReferenceBaseSizeResolved)
        {
            double rasterizationScale = ResolveRasterizationScale(entry);
            entry.ReferenceBaseSize = new SizeInt32(
                (int)Math.Round(entry.RawBaseSize.Width * ReferenceRasterizationScale / rasterizationScale),
                (int)Math.Round(entry.RawBaseSize.Height * ReferenceRasterizationScale / rasterizationScale));
            entry.ReferenceBaseSizeResolved = true;
        }
    }

    private void ApplyEntry(ScaledWindow entry)
    {
        double rasterizationScale = ResolveRasterizationScale(entry);
        entry.EffectiveScale = CurrentScale * ReferenceRasterizationScale / rasterizationScale;

        ApplyTransform(entry);
        ApplyWindowSize(entry);
    }

    private void ApplyTransform(ScaledWindow entry)
    {
        entry.Content.RenderTransform = IsUnscaled(entry.EffectiveScale)
            ? null
            : new ScaleTransform
            {
                ScaleX = entry.EffectiveScale,
                ScaleY = entry.EffectiveScale
            };
        ApplyLayoutSize(entry, new Size(entry.Host.ActualWidth, entry.Host.ActualHeight));
    }

    /// <summary>
    /// Gives the content the size it would have had on the reference display. The
    /// host reports its own measured size, so no conversion between DIPs and
    /// physical pixels is involved.
    /// </summary>
    private void ApplyLayoutSize(ScaledWindow entry, Size hostSize)
    {
        if (IsUnscaled(entry.EffectiveScale))
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
        entry.Content.Width = Math.Max(0.0, hostSize.Width / entry.EffectiveScale);
        entry.Content.Height = Math.Max(0.0, hostSize.Height / entry.EffectiveScale);
    }

    private void ApplyWindowSize(ScaledWindow entry)
    {
        if (!entry.ResizeWithScale ||
            !entry.ReferenceBaseSizeResolved ||
            entry.ReferenceBaseSize.Width <= 0 ||
            entry.ReferenceBaseSize.Height <= 0)
        {
            return;
        }

        SizeInt32 targetSize = new(
            (int)Math.Round(entry.ReferenceBaseSize.Width * CurrentScale),
            (int)Math.Round(entry.ReferenceBaseSize.Height * CurrentScale));

        if (entry.Window.AppWindow.Size.Width != targetSize.Width ||
            entry.Window.AppWindow.Size.Height != targetSize.Height)
        {
            entry.Window.AppWindow.Resize(targetSize);
        }
    }

    private ScaledWindow? FindByHost(Grid host)
    {
        return attachedWindows.Values.FirstOrDefault(
            entry => ReferenceEquals(entry.Host, host));
    }

    private static double ResolveRasterizationScale(ScaledWindow entry)
    {
        double rasterizationScale = entry.XamlRoot?.RasterizationScale
            ?? ReferenceRasterizationScale;
        return double.IsFinite(rasterizationScale) && rasterizationScale > 0.0
            ? rasterizationScale
            : ReferenceRasterizationScale;
    }

    private static bool IsUnscaled(double scale)
    {
        return Math.Abs(scale - DefaultScale) <= ScaleEpsilon;
    }

    private static double NormalizeScaleChoice(double scale)
    {
        if (!double.IsFinite(scale))
        {
            return DefaultScale;
        }

        return ManualScales.MinBy(candidate => Math.Abs(candidate - scale));
    }

    private sealed class ScaledWindow
    {
        public ScaledWindow(
            Window window,
            Grid host,
            FrameworkElement content,
            bool resizeWithScale,
            SizeInt32 rawBaseSize,
            bool baseSizeTracksSystemDpi)
        {
            Window = window;
            Host = host;
            Content = content;
            ResizeWithScale = resizeWithScale;
            RawBaseSize = rawBaseSize;
            ReferenceBaseSize = rawBaseSize;
            ReferenceBaseSizeResolved = !baseSizeTracksSystemDpi;
        }

        public Window Window { get; }

        public Grid Host { get; }

        public FrameworkElement Content { get; }

        public bool ResizeWithScale { get; }

        public SizeInt32 RawBaseSize { get; }

        public SizeInt32 ReferenceBaseSize { get; set; }

        public bool ReferenceBaseSizeResolved { get; set; }

        public XamlRoot? XamlRoot { get; set; }

        public double EffectiveScale { get; set; } = DefaultScale;
    }

}
