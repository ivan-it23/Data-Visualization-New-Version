using AMK.DataVisualization.App.Services;
using AMK.DataVisualization.DeviceRegistry;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace AMK.DataVisualization.App.Views;

/// <summary>
/// Настройка связи с базой учёта приборов: адрес сервера и токен доступа,
/// созданный пользователем в своём профиле на сайте.
/// </summary>
public sealed partial class DeviceRegistryWindow : Window
{
    private readonly DeviceRegistryConnection connection;
    private bool allowPermanentClose;
    private bool activated;

    internal DeviceRegistryWindow(DeviceRegistryConnection connection)
    {
        this.connection = connection;
        InitializeComponent();
        // Высота подобрана под фактическое содержимое: последняя строка сетки
        // пустая и забирает остаток, поэтому лишняя высота превращалась в
        // пустое место под строкой состояния.
        AppWindow.Resize(new SizeInt32(660, 455));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;

            // Сжатие запрещено: при уменьшении окна поля ввода и строка
            // состояния перестают помещаться.
            presenter.IsResizable = false;
        }

        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowForOwner(IntPtr ownerHandle)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        SecondaryWindowIntegration.ConfigureForOwner(windowHandle, ownerHandle);

        LoadSettingsIntoControls();

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

    private void LoadSettingsIntoControls()
    {
        DeviceRegistrySettings settings = connection.Settings;
        ServerUrlTextBox.Text = settings.ServerUrl;
        TokenPasswordBox.Password = settings.Token;
        StatusTextBlock.Text = settings.IsConfigured
            ? "Токен сохранён. Нажмите «Проверить соединение», чтобы убедиться, что он действует."
            : "Токен не задан: загрузка файлов в базу недоступна.";
    }

    private async void CheckConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceRegistrySettings candidate = new()
        {
            ServerUrl = ServerUrlTextBox.Text.Trim(),
            Token = TokenPasswordBox.Password.Trim()
        };

        if (!candidate.IsConfigured)
        {
            await ReportAsync(
                "Проверка невозможна",
                "Укажите адрес сервера и токен доступа.");
            return;
        }

        SetBusy(true);
        StatusTextBlock.Text = "Проверка соединения…";
        try
        {
            using DeviceRegistryClient client = new(candidate);
            DeviceRegistryResult<RegistryUser> result =
                await client.WhoAmIAsync(CancellationToken.None);
            if (result.Success && result.Value is not null)
            {
                await ReportAsync(
                    "Связь установлена",
                    $"Загрузки будут выполняться от имени: {DescribeUser(result.Value)}.");
            }
            else
            {
                await ReportAsync("Нет связи с базой приборов", result.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string serverUrl = ServerUrlTextBox.Text.Trim();
        string token = TokenPasswordBox.Password.Trim();

        if (serverUrl.Length == 0)
        {
            await ReportAsync("Настройки не сохранены", "Укажите адрес сервера.");
            return;
        }

        if (!connection.Save(serverUrl, token))
        {
            await ReportAsync(
                "Настройки не сохранены",
                "Не удалось записать файл настроек в профиле пользователя.");
            return;
        }

        await ReportAsync(
            "Настройки сохранены",
            token.Length > 0
                ? "Токен сохранён и будет использоваться в следующих сессиях."
                : "Токен не задан, поэтому загрузка файлов в базу недоступна.");
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TokenPasswordBox.Password = string.Empty;
        if (!connection.Save(ServerUrlTextBox.Text.Trim(), string.Empty))
        {
            await ReportAsync(
                "Токен не удалён",
                "Не удалось записать файл настроек в профиле пользователя.");
            return;
        }

        await ReportAsync("Токен удалён", "Загрузка файлов в базу теперь недоступна.");
    }

    /// <summary>
    /// Показывает итог операции и оставляет его же в строке состояния: диалог
    /// нельзя не заметить, а строка сохраняет результат после его закрытия.
    /// </summary>
    private async Task ReportAsync(string title, string message)
    {
        StatusTextBlock.Text = message;

        ContentDialog dialog = new()
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "ОК"
        };

        await dialog.ShowAsync();
    }

    private void SetBusy(bool busy)
    {
        CheckConnectionButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
    }

    private static string DescribeUser(RegistryUser user)
    {
        return string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;
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
