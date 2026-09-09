using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Панель, у которой масштаб содержимого участвует в разметке, а не только в
/// отрисовке.
/// </summary>
/// <remarks>
/// Обычное преобразование <see cref="ScaleTransform"/> на разметку не влияет:
/// родитель по-прежнему видит исходный размер элемента. Для содержимого окна
/// это неважно — там размер задаёт кадр окна, — но всплывающая поверхность
/// (диалог, флайаут) подгоняется под желаемый размер содержимого, и увеличенное
/// преобразованием содержимое вылезло бы за её границы.
///
/// Поэтому панель делает обе вещи сразу: сообщает родителю размер, умноженный
/// на масштаб, и раскладывает ребёнка в исходных единицах, а преобразование
/// доводит его до объявленного размера. Ребёнок при этом измеряется в
/// уменьшенном доступном пространстве, поэтому перенос строк и прокрутка
/// срабатывают там же, где сработали бы на эталонном дисплее.
///
/// Размер верен с первого измерения: окно popup'а строится уже по нему, ничего
/// не обрезается и не меняется в ответ на посторонний ввод.
/// </remarks>
internal sealed class ScaledContentHost : Panel
{
    private readonly double scale;

    public ScaledContentHost(UIElement content, double scale)
    {
        this.scale = scale;
        content.RenderTransformOrigin = new Point(0, 0);
        content.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
        Children.Add(content);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UIElement child = Children[0];
        child.Measure(new Size(Shrink(availableSize.Width), Shrink(availableSize.Height)));
        Size desired = child.DesiredSize;
        return new Size(desired.Width * scale, desired.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElement child = Children[0];
        child.Arrange(new Rect(0, 0, Shrink(finalSize.Width), Shrink(finalSize.Height)));
        return finalSize;
    }

    private double Shrink(double value)
    {
        return double.IsInfinity(value) ? value : value / scale;
    }
}

/// <summary>
/// Приведение всплывающих поверхностей — диалогов и флайаутов — к масштабу
/// остального интерфейса.
/// </summary>
/// <remarks>
/// <see cref="ContentDialog"/> и <see cref="Flyout"/> разворачиваются в
/// popup-слое окна, куда преобразование содержимого окна не достаёт: на дисплее
/// с системным масштабом, отличным от эталонных 125 %, они оставались в
/// системном размере и выглядели мельче остального интерфейса.
///
/// Приводятся они тем же способом, что и пункты меню (<c>ScaleMenuItem</c>
/// главного окна): масштабируется разметка, а не отрисовка. Содержимое
/// оборачивается в <see cref="ScaledContentHost"/>, а те части, которые рисует
/// шаблон, — заголовок, кнопки, отступы и предельные размеры — задаются явно.
///
/// Исходные величины берутся из ресурсов темы, а не из констант в коде: тема —
/// их единственный источник, и при её смене они изменятся вместе с ней.
/// Значения в <c>Fallback</c> — то, что фактически отдала тема WinUI при
/// проверке запуском; они нужны только на случай, если ключ пропадёт.
///
/// На эталонной машине (масштаб равен единице) не выполняется ничего: диалог
/// остаётся ровно таким, каким его строит тема.
/// </remarks>
internal static class PopupScale
{
    /// <summary>
    /// Ключ-отметка о том, что к диалогу уже применён масштаб. Диалог может
    /// показываться повторно — окно подтверждения загрузки в базу показывается
    /// в цикле, пока имя файла не примут, — а масштабировать его дважды нельзя.
    /// </summary>
    private const string AppliedMarker = "AmkPopupScaleApplied";

    /// <summary>
    /// Размерные ресурсы шаблона диалога. Значение берётся у самого диалога,
    /// если вызывающий его переопределил, затем из ресурсов приложения и только
    /// в последнюю очередь из запасного значения.
    /// </summary>
    private static readonly (string Key, double Fallback)[] DialogSizeResources =
    [
        ("ContentDialogMinWidth", 320.0),
        ("ContentDialogMaxWidth", 548.0),
        ("ContentDialogMinHeight", 184.0),
        ("ContentDialogMaxHeight", 756.0),
        ("ContentDialogTitleMaxHeight", 56.0),
        ("ContentDialogButtonHeight", 32.0)
    ];

    /// <summary>
    /// Кегль заголовка диалога. Единственная величина, которой в теме нет
    /// отдельного ключа: шаблон задаёт её числом.
    /// </summary>
    private const double DialogTitleFontSize = 20.0;

    private const double ScaleEpsilon = 1e-9;

    /// <summary>
    /// Приводит диалог к масштабу интерфейса. Вызывается перед показом, когда
    /// масштаб монитора уже известен.
    /// </summary>
    public static void Apply(ContentDialog dialog)
    {
        double scale = WindowScaleService.PopupContentScale;
        if (!NeedsScaling(scale) || dialog.Resources.ContainsKey(AppliedMarker))
        {
            return;
        }

        dialog.Resources[AppliedMarker] = true;

        // Предельные размеры масштабируются вместе с содержимым: иначе
        // увеличенное содержимое упёрлось бы в прежний ContentDialogMaxWidth и
        // было бы сжато обратно.
        foreach ((string key, double fallback) in DialogSizeResources)
        {
            dialog.Resources[key] = ResolveDouble(dialog, key, fallback) * scale;
        }

        dialog.Resources["ContentDialogPadding"] = ScaleThickness(
            ResolveThickness(dialog, "ContentDialogPadding", new Thickness(24)),
            scale);

        if (dialog.Content is UIElement content and not ScaledContentHost)
        {
            dialog.Content = new ScaledContentHost(content, scale);
        }

        // Заголовок рисует шаблон, вне содержимого, поэтому он заменяется
        // разметкой, которую можно отмасштабировать. Кегль и начертание заданы
        // явно: обёртка умножает их сама, а полагаться на наследование от
        // заголовочного элемента шаблона нельзя — оно зависит от его устройства.
        if (dialog.Title is string title && title.Length > 0)
        {
            dialog.Title = new ScaledContentHost(
                new TextBlock
                {
                    Text = title,
                    FontSize = DialogTitleFontSize,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                scale);
        }

        ApplyButtonStyles(dialog, scale);
    }

    /// <summary>
    /// Очередь показа диалогов. WinUI допускает только один открытый
    /// <see cref="ContentDialog"/>, и второй показ падает исключением
    /// «Only a single ContentDialog can be open at any time».
    /// </summary>
    /// <remarks>
    /// Так и произошло при загрузке файла в базу: пока был открыт один диалог,
    /// поток загрузки попытался показать выбор прибора. Исключение уходило из
    /// обработчика <c>async void</c> мимо кода окна и роняло приложение.
    ///
    /// Очередь делает столкновение невозможным: второй диалог ждёт, пока
    /// пользователь ответит на первый, и показывается сразу после него. Это то
    /// же поведение, которого пользователь ждёт от модальных окон, и оно не
    /// зависит от того, из какого окна пришёл вызов.
    /// </remarks>
    private static readonly SemaphoreSlim DialogGate = new(1, 1);

    /// <summary>
    /// Показывает диалог, предварительно приведя его к масштабу интерфейса.
    /// Диалоги показываются по одному.
    /// </summary>
    public static async Task<ContentDialogResult> ShowScaledAsync(this ContentDialog dialog)
    {
        Apply(dialog);
        await DialogGate.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            DialogGate.Release();
        }
    }

    /// <summary>
    /// Приводит флайаут к масштабу интерфейса.
    /// </summary>
    public static void Apply(Flyout flyout)
    {
        double scale = WindowScaleService.PopupContentScale;
        if (!NeedsScaling(scale))
        {
            return;
        }

        if (flyout.Content is UIElement content and not ScaledContentHost)
        {
            flyout.Content = new ScaledContentHost(content, scale);
        }

        // Отступ и предельные размеры рисует презентер, а он живёт вне обёртки.
        // Без этого увеличенное содержимое упёрлось бы в прежний MaxWidth и
        // было бы перенесено по строкам.
        flyout.FlyoutPresenterStyle ??= CreateFlyoutPresenterStyle(scale);
    }

    private static bool NeedsScaling(double scale)
    {
        return scale > 0.0 && Math.Abs(scale - 1.0) > ScaleEpsilon;
    }

    /// <summary>
    /// Задаёт размер кнопок диалога. Их текст задаётся строкой, поэтому размер
    /// приходится задавать стилем — так же, как высота и кегль пунктов меню.
    /// </summary>
    /// <remarks>
    /// Размер задаётся <b>локальными значениями</b> самих кнопок, а не стилем.
    /// Стилем нельзя: кнопка по умолчанию получает акцентный вид от состояния
    /// шаблона, которое подменяет ей <c>Style</c> целиком, и любые размерные
    /// сеттеры вместе с ним пропадают. Так «Далее» оставалась в системном
    /// размере, когда «Отмена» рядом уже была отмасштабирована. Подсунуть
    /// шаблону масштабированный <c>AccentButtonStyle</c> через ресурсы диалога
    /// тоже нельзя: ссылка внутри шаблона разрешается в его собственной
    /// области, а не в ресурсах экземпляра.
    ///
    /// Локальное значение старше любого стиля, поэтому переживает подмену и не
    /// затрагивает оформление кнопки — меняются только кегль, высота и отступы.
    ///
    /// Момент выбран так, чтобы размер был верен с первой отрисовки: к
    /// <see cref="FrameworkElement.Loaded"/> шаблон уже применён и состояние
    /// кнопки по умолчанию уже отработало, а кадр ещё не показан.
    /// </remarks>
    private static void ApplyButtonStyles(ContentDialog dialog, double scale)
    {
        double fontSize = ResolveDouble(dialog, "ControlContentThemeFontSize", 14.0) * scale;
        double minHeight = ResolveDouble(dialog, "ContentDialogButtonHeight", 32.0) * scale;
        Thickness padding = ScaleThickness(
            ResolveThickness(dialog, "ButtonPadding", new Thickness(11, 5, 11, 6)),
            scale);

        dialog.Loaded += (_, _) =>
        {
            foreach (string name in DialogButtonNames)
            {
                if (FindTemplateChild(dialog, name) is Button button)
                {
                    button.FontSize = fontSize;
                    button.MinHeight = minHeight;
                    button.Padding = padding;
                }
            }
        };
    }

    /// <summary>Имена кнопок в шаблоне <see cref="ContentDialog"/>.</summary>
    private static readonly string[] DialogButtonNames =
    [
        "PrimaryButton",
        "SecondaryButton",
        "CloseButton"
    ];

    /// <summary>
    /// Ищет именованный элемент шаблона. В содержимое, уже обёрнутое
    /// <see cref="ScaledContentHost"/>, поиск не заходит: там масштаб задан
    /// обёрткой, и трогать элементы повторно нельзя.
    /// </summary>
    private static FrameworkElement? FindTemplateChild(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is ScaledContentHost)
            {
                continue;
            }

            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindTemplateChild(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Style? CreateFlyoutPresenterStyle(double scale)
    {
        Style? basedOn = ResolveStyle("DefaultFlyoutPresenterStyle");
        if (basedOn is null)
        {
            return null;
        }

        Style style = new(typeof(FlyoutPresenter)) { BasedOn = basedOn };
        style.Setters.Add(new Setter(
            FrameworkElement.MaxWidthProperty,
            ResolveDouble(dialog: null, "FlyoutThemeMaxWidth", 456.0) * scale));
        style.Setters.Add(new Setter(
            FrameworkElement.MaxHeightProperty,
            ResolveDouble(dialog: null, "FlyoutThemeMaxHeight", 758.0) * scale));

        // У отступа презентера отдельного ключа в теме нет — он задан прямо в
        // штатном стиле, оттуда и читается.
        style.Setters.Add(new Setter(
            Control.PaddingProperty,
            ScaleThickness(
                ResolveSetterThickness(basedOn, Control.PaddingProperty, new Thickness(16, 15, 16, 17)),
                scale)));
        return style;
    }

    private static Style? ResolveStyle(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out object? value) ? value as Style : null;
    }

    private static double ResolveDouble(ContentDialog? dialog, string key, double fallback)
    {
        return Resolve(dialog, key, fallback);
    }

    private static Thickness ResolveThickness(ContentDialog? dialog, string key, Thickness fallback)
    {
        return Resolve(dialog, key, fallback);
    }

    private static T Resolve<T>(ContentDialog? dialog, string key, T fallback)
    {
        if (dialog is not null && dialog.Resources.TryGetValue(key, out object? local) && local is T localValue)
        {
            return localValue;
        }

        return Application.Current.Resources.TryGetValue(key, out object? shared) && shared is T sharedValue
            ? sharedValue
            : fallback;
    }

    /// <summary>
    /// Читает величину, заданную сеттером штатного стиля. Сеттер может нести
    /// не значение, а привязку к ресурсу темы — такой сеттер пропускается.
    /// </summary>
    private static Thickness ResolveSetterThickness(Style style, DependencyProperty property, Thickness fallback)
    {
        foreach (SetterBase setterBase in style.Setters)
        {
            if (setterBase is Setter setter
                && setter.Property == property
                && setter.Value is Thickness value)
            {
                return value;
            }
        }

        return fallback;
    }

    private static Thickness ScaleThickness(Thickness value, double scale)
    {
        return new Thickness(
            value.Left * scale,
            value.Top * scale,
            value.Right * scale,
            value.Bottom * scale);
    }
}
