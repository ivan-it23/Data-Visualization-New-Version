using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Application-wide zoom of the whole window content. A manual 100% reproduces
/// the physical UI size of the 125%-DPI reference display, independently for
/// every window and monitor. The user can choose one smaller or larger preset.
///
/// Выпадающие списки, меню и диалоги живут в отдельном popup-слое окна, куда
/// преобразование содержимого не достаёт. Их размер задаётся при построении
/// самих поверхностей через <see cref="PopupContentScale"/>: у части popup'ов
/// собственное окно, и преобразование отрисовки на них либо не действует, либо
/// накладывается поверх уже применённого — предсказуемо работает только
/// масштабирование разметки.
/// </summary>
internal sealed class WindowScaleService
{
    public const double DefaultScale = 1.0;
    public const double ReferenceRasterizationScale = 1.25;

    /// <summary>
    /// Ширина холста, на которую рассчитана компоновка главного окна, в
    /// аппаратно-независимых пикселях. Ровно столько даёт эталонный дисплей:
    /// 1920 физических пикселей при масштабе Windows 125 %.
    /// </summary>
    /// <remarks>
    /// Ограничение только по ширине. По вертикали компоновка тянется — графики
    /// делят остаток, — а по горизонтали верхняя панель управления состоит из
    /// элементов фиксированной ширины, и именно она уходит за край, если холст
    /// уже эталонного.
    /// </remarks>
    public const double ReferenceCanvasWidth = 1536.0;

    // Пределы подобраны по тому, как окна ведут себя на деле: при 125 % часть
    // элементов не помещалась на экран, при 75 % становилась слишком мелкой.
    // Сохранённый ранее выбор не теряется: NormalizeScaleChoice приводит его к
    // ближайшему доступному, то есть 125 % становится 115 %, а 75 % — 85 %.
    public const double MinimumScale = 0.85;
    public const double MaximumScale = 1.15;

    private const double ScaleEpsilon = 1e-9;

    /// <summary>Точек на дюйм, соответствующих системному масштабу 100 %.</summary>
    private const double DefaultDpi = 96.0;

    /// <summary>
    /// На сколько холст может не дотянуть до эталонного, чтобы это
    /// ещё считалось совпадением.
    /// </summary>
    private const double FitToleranceDips = 16.0;

    /// <summary>
    /// Значения масштаба в порядке отображения: уменьшение, значение по
    /// умолчанию, увеличение.
    /// </summary>
    public static IReadOnlyList<double> ManualScales { get; } = [MinimumScale, DefaultScale, MaximumScale];

    private readonly Dictionary<Window, ScaledWindow> attachedWindows = [];

    public event EventHandler? ScaleChanged;

    public WindowScaleSettings Settings { get; private set; } = new();

    public double CurrentScale { get; private set; } = DefaultScale;

    public double ManualScale => Settings.ManualScale;

    /// <summary>
    /// Масштаб, который поверхности всплывающих меню обязаны заложить в свою
    /// разметку, чтобы совпасть с остальным интерфейсом.
    /// </summary>
    /// <remarks>
    /// Всплывающие поверхности живут в отдельном слое окна, и ведут себя они
    /// по-разному. Список <c>ComboBox</c> разворачивается внутри окна и
    /// преобразование содержимого наследует — его трогать не нужно и нельзя,
    /// иначе масштаб умножится на себя. А <c>MenuFlyout</c> получает собственное
    /// окно, куда преобразование не попадает, и остаётся в системном размере.
    ///
    /// Поэтому меню масштабируются не преобразованием отрисовки, а разметкой:
    /// высота, кегль и отступы пунктов задаются при построении меню. Так размер
    /// верен с первой отрисовки, окно popup'а строится уже по нему — ничего не
    /// обрезается и ничто не меняется в ответ на посторонний ввод.
    /// </remarks>
    /// <remarks>
    /// Значение общее для приложения: главное окно задаёт его, а пользуются им
    /// все всплывающие поверхности, включая построенные во вторичных окнах,
    /// у которых ссылки на службу нет. Экземпляр службы в приложении один —
    /// его создаёт главное окно.
    /// </remarks>
    public static double PopupContentScale { get; private set; } = DefaultScale;

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
    /// scaled twice. Окно, содержимое которого заполняет его целиком, передаёт
    /// <paramref name="fitContentToWindow"/>: приведение к эталону тогда не
    /// перерастает фактическую ширину экрана.
    /// </summary>
    public void Attach(
        Window window,
        bool resizeWithScale,
        bool baseSizeTracksSystemDpi = false,
        bool fitContentToWindow = false)
    {
        if (attachedWindows.ContainsKey(window) || window.Content is not FrameworkElement content)
        {
            return;
        }

        SizeInt32 baseSize = window.AppWindow.Size;
        window.Content = null;

        // Слоёв два, и это существенно. Внешний растянут по окну, внутренний
        // несёт масштаб. Преобразование ставится на внутренний — он создан
        // здесь же и в разметке ещё не участвовал. Раньше масштаб ставился
        // прямо на содержимое окна, то есть на элемент, который уже прошёл
        // разметку и был перепривязан к новому родителю: у окон фиксированного
        // размера такое преобразование выставлялось, но на отрисовку не
        // влияло — содержимое занимало 1/1.25 окна, оставляя пустое место
        // справа и снизу.
        Grid host = new();
        Grid scaler = new();
        scaler.RenderTransformOrigin = new Point(0, 0);
        scaler.HorizontalAlignment = HorizontalAlignment.Left;
        scaler.VerticalAlignment = VerticalAlignment.Top;
        scaler.Children.Add(content);
        host.Children.Add(scaler);
        window.Content = host;

        ScaledWindow entry = new(
            window,
            WindowNative.GetWindowHandle(window),
            host,
            scaler,
            resizeWithScale,
            baseSize,
            baseSizeTracksSystemDpi,
            fitContentToWindow);
        attachedWindows[window] = entry;

        // Эталонный размер кадра выводится сразу: масштаб монитора уже известен
        // из окна, ждать XamlRoot незачем.
        ResolveReferenceBaseSize(entry);

        host.Loaded += Host_Loaded;
        host.SizeChanged += Host_SizeChanged;
        window.Activated += Window_Activated;
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
        if (sender is not Grid host || FindByHost(host) is not { } entry)
        {
            return;
        }

        // Пересчитывается всё целиком, а не только размер содержимого.
        // Преобразование и размер — две половины одного масштаба, и задавать их
        // порознь нельзя: окно, у которого размер задан, а преобразование нет,
        // рисует содержимое в 1/1.25 своей ширины и обрезает его по этой
        // границе — именно так выглядело окно «Связь с БД».
        //
        // Цикла здесь нет: размер содержимого на размер хоста не влияет, а
        // ApplyWindowSize меняет кадр только тогда, когда он действительно
        // отличается от нужного.
        ApplyEntry(entry);
    }

    /// <summary>
    /// Пересчитывает масштаб при первой активации окна.
    /// </summary>
    /// <remarks>
    /// Немодальное окно приложения к моменту первого прохода разметки ещё не
    /// имеет окончательных размеров: их доводят настройка владельца и
    /// центрирование над ним. Единственный SizeChanged к этому моменту уже
    /// прошёл, а неизменяемому окну второго не будет — содержимое так и
    /// осталось бы посчитанным по промежуточному размеру и не заполняло бы
    /// окно.
    /// </remarks>
    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (sender is Window window && attachedWindows.TryGetValue(window, out ScaledWindow? entry))
        {
            ApplyEntry(entry);
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
        window.Activated -= Window_Activated;
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

        ResolveReferenceBaseSize(entry);
    }

    /// <summary>
    /// Переводит исходный размер кадра окна в размер, который он имел бы на
    /// эталонном дисплее. Выполняется один раз: дальше кадр считается от него.
    /// </summary>
    private static void ResolveReferenceBaseSize(ScaledWindow entry)
    {
        if (entry.ReferenceBaseSizeResolved)
        {
            return;
        }

        double rasterizationScale = ResolveRasterizationScale(entry);
        entry.ReferenceBaseSize = new SizeInt32(
            (int)Math.Round(entry.RawBaseSize.Width * ReferenceRasterizationScale / rasterizationScale),
            (int)Math.Round(entry.RawBaseSize.Height * ReferenceRasterizationScale / rasterizationScale));
        entry.ReferenceBaseSizeResolved = true;
    }

    private void ApplyEntry(ScaledWindow entry)
    {
        entry.EffectiveScale = CurrentScale * ResolveBaseScale(entry);

        // Меню строит главное окно, оно же задаёт масштаб для их разметки.
        if (entry.FitContentToWindow)
        {
            PopupContentScale = entry.EffectiveScale;
        }

        ApplyTransform(entry);
        ApplyWindowSize(entry);
    }

    /// <summary>
    /// Множитель, который соответствует выбранным 100 %: приведение
    /// системного масштаба к эталонному, ограниченное тем, что реально
    /// помещается в окно.
    /// </summary>
    /// <remarks>
    /// На экране не меньше эталонного ограничение ничего не меняет и
    /// 100 % воспроизводит физический размер эталона. На экране уже
    /// эталонного — например, на ноутбуке 1366×768 — эталонный размер
    /// физически недостижим, и без ограничения верхняя панель ушла бы за край.
    ///
    /// Ограничивается именно база, а не итоговый масштаб: выбрав 115 %,
    /// пользователь сознательно меняет обзор на крупность, и отменять этот
    /// выбор приложение не вправе — на эталонной машине 115 % ведёт себя так же.
    /// </remarks>
    private static double ResolveBaseScale(ScaledWindow entry)
    {
        double referenceScale = ReferenceRasterizationScale / ResolveRasterizationScale(entry);
        if (!entry.FitContentToWindow)
        {
            return referenceScale;
        }

        double hostWidth = entry.Host.ActualWidth;
        if (hostWidth <= 0.0)
        {
            // Размера ещё нет — ограничивать нечем. Первый же SizeChanged пересчитает.
            return referenceScale;
        }

        // На эталонном дисплее ширина холста равна эталонной ровно, без запаса.
        // Без допуска лишний пиксель рамки давал бы масштаб вроде 0.996:
        // на глаз незаметно, но это уже не пиксель в пиксель, а пересчёт
        // всего содержимого через преобразование.
        double canvasAtReference = hostWidth / referenceScale;
        if (canvasAtReference >= ReferenceCanvasWidth - FitToleranceDips)
        {
            return referenceScale;
        }

        return hostWidth / ReferenceCanvasWidth;
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

    /// <summary>
    /// Системный масштаб монитора, на котором сейчас находится окно.
    /// </summary>
    /// <remarks>
    /// <c>XamlRoot</c> появляется только к первому проходу разметки, а масштаб
    /// нужен уже в <see cref="Attach"/>: иначе первый кадр рисуется без
    /// компенсации и пользователь видит, как интерфейс скачком меняет размер.
    /// Поэтому до появления <c>XamlRoot</c> масштаб берётся у самого окна через
    /// <c>GetDpiForWindow</c> — это тот же источник, из которого его выводит и
    /// сам WinUI.
    ///
    /// Эталон подставляется только тогда, когда не ответил ни один источник:
    /// он даёт <c>EffectiveScale = 1</c>, то есть поведение без компенсации.
    /// </remarks>
    private static double ResolveRasterizationScale(ScaledWindow entry)
    {
        if (IsUsableScale(entry.XamlRoot?.RasterizationScale, out double xamlRootScale))
        {
            return xamlRootScale;
        }

        if (entry.WindowHandle != IntPtr.Zero)
        {
            uint dpi = GetDpiForWindow(entry.WindowHandle);
            if (dpi > 0 && IsUsableScale(dpi / DefaultDpi, out double windowScale))
            {
                return windowScale;
            }
        }

        return ReferenceRasterizationScale;
    }

    private static bool IsUsableScale(double? candidate, out double scale)
    {
        scale = candidate ?? 0.0;
        return candidate is not null && double.IsFinite(scale) && scale > 0.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

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
            IntPtr windowHandle,
            Grid host,
            FrameworkElement content,
            bool resizeWithScale,
            SizeInt32 rawBaseSize,
            bool baseSizeTracksSystemDpi,
            bool fitContentToWindow)
        {
            Window = window;
            WindowHandle = windowHandle;
            Host = host;
            Content = content;
            ResizeWithScale = resizeWithScale;
            FitContentToWindow = fitContentToWindow;
            RawBaseSize = rawBaseSize;
            ReferenceBaseSize = rawBaseSize;
            ReferenceBaseSizeResolved = !baseSizeTracksSystemDpi;
        }

        public Window Window { get; }

        /// <summary>
        /// Дескриптор окна. Нужен, чтобы узнать системный масштаб монитора до
        /// того, как у содержимого появится <c>XamlRoot</c>.
        /// </summary>
        public IntPtr WindowHandle { get; }

        public Grid Host { get; }

        public FrameworkElement Content { get; }

        public bool ResizeWithScale { get; }

        /// <summary>
        /// Содержимое занимает окно целиком, поэтому масштаб ограничен
        /// тем, что фактически помещается в это окно.
        /// </summary>
        public bool FitContentToWindow { get; }

        public SizeInt32 RawBaseSize { get; }

        public SizeInt32 ReferenceBaseSize { get; set; }

        public bool ReferenceBaseSizeResolved { get; set; }

        public XamlRoot? XamlRoot { get; set; }

        public double EffectiveScale { get; set; } = DefaultScale;
    }

}
