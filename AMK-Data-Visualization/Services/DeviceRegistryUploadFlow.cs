using AMK.DataVisualization.Core;
using AMK.DataVisualization.DeviceRegistry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Что именно выгружается в базу.
/// </summary>
/// <param name="Kind">Раздел карточки прибора.</param>
/// <param name="Content">Содержимое файла ровно в том виде, в каком оно попало бы на диск.</param>
/// <param name="Extension">Расширение предлагаемого имени файла.</param>
/// <param name="SerialNumber">
/// Серийный номер прибора. Пусто для файла данных, открытого без прибора и без
/// метрологии: в кадре измерений серийного номера нет, есть только сигнатура.
/// </param>
/// <param name="DeviceType">Тип прибора из сигнатуры.</param>
/// <param name="TransmitterCount">Число передатчиков из сигнатуры.</param>
/// <param name="Description">Строка описания источника для окна подтверждения.</param>
internal sealed record UploadSource(
    UploadKind Kind,
    byte[] Content,
    string Extension,
    uint? SerialNumber,
    DeviceType DeviceType,
    int TransmitterCount,
    string Description);

/// <summary>
/// Полный сценарий загрузки файла в базу учёта: определение прибора,
/// подтверждение пользователем, отправка и разбор ответа.
///
/// Живёт отдельно от окон, потому что одинаково нужен и окну метрологии,
/// и главному окну.
/// </summary>
internal sealed class DeviceRegistryUploadFlow
{
    private readonly DeviceRegistryConnection connection;

    public DeviceRegistryUploadFlow(DeviceRegistryConnection connection)
    {
        this.connection = connection;
    }

    public async Task RunAsync(XamlRoot xamlRoot, UploadSource source)
    {
        if (!connection.IsConfigured)
        {
            await ShowMessageAsync(
                xamlRoot,
                "Связь с БД не настроена",
                "Укажите адрес сервера и токен доступа в окне «Связь с БД». " +
                "Токен создаётся на сайте учёта приборов в разделе «Профиль».");
            return;
        }

        if (source.Content.Length == 0)
        {
            await ShowMessageAsync(xamlRoot, "Нечего загружать", "Файл пуст.");
            return;
        }

        using DeviceRegistryClient client = connection.CreateClient();

        DeviceResolution resolution = await ResolveDeviceAsync(xamlRoot, client, source);
        if (resolution.Device is null)
        {
            if (resolution.Message.Length > 0)
            {
                await ShowMessageAsync(xamlRoot, "Прибор не определён", resolution.Message);
            }

            return;
        }

        RegistryDevice device = resolution.Device;
        string suggestedName = UploadFileName.Build(source.Kind, device, DateTimeOffset.Now, source.Extension);

        UploadConfirmation? confirmation = await ConfirmAsync(
            xamlRoot,
            source,
            device,
            suggestedName,
            resolution.Warnings);
        if (confirmation is null)
        {
            return;
        }

        UploadRequest request = new(source.Kind, device, confirmation.FileName, source.Content);
        DeviceRegistryResult<RegistryFile> result = await client.UploadAsync(request, CancellationToken.None);

        // Повторная отправка того же содержимого — штатная ситуация, а не ошибка:
        // пользователь мог нажать кнопку дважды или вернуться к тому же файлу.
        if (!result.Success && result.Code == DeviceRegistryCodes.Duplicate)
        {
            string existing = result.ExistingFile is null
                ? string.Empty
                : $"\n\nВ базе: {result.ExistingFile.FileName}" +
                  (result.ExistingFile.CreatedAt is null
                      ? string.Empty
                      : $" от {result.ExistingFile.CreatedAt:dd.MM.yyyy HH:mm}");

            bool uploadAnyway = await ConfirmSimpleAsync(
                xamlRoot,
                "Файл уже загружен",
                result.Message + existing + "\n\nЗагрузить ещё одну копию?",
                "Загрузить повторно");
            if (!uploadAnyway)
            {
                return;
            }

            result = await client.UploadAsync(request with { Force = true }, CancellationToken.None);
        }

        if (!result.Success)
        {
            await ShowMessageAsync(xamlRoot, "Файл не загружен", result.Message);
            return;
        }

        await ShowMessageAsync(
            xamlRoot,
            "Файл загружен",
            $"«{confirmation.FileName}» добавлен в раздел «{source.Kind.ToDisplayName()}» " +
            $"карточки прибора {device.DisplayName}.");
    }

    // ------------------------------------------------------------------

    private sealed record DeviceResolution(
        RegistryDevice? Device,
        string Message,
        IReadOnlyList<string> Warnings);

    private async Task<DeviceResolution> ResolveDeviceAsync(
        XamlRoot xamlRoot,
        DeviceRegistryClient client,
        UploadSource source)
    {
        if (source.SerialNumber is uint serial)
        {
            DeviceRegistryResult<RegistryDevice> resolved = await client.ResolveDeviceAsync(
                serial,
                source.DeviceType,
                source.TransmitterCount,
                CancellationToken.None);

            if (resolved.Success && resolved.Value is not null)
            {
                return new DeviceResolution(resolved.Value, string.Empty, resolved.Warnings);
            }

            // Серийный номер уникален, поэтому несколько совпадений означают
            // расхождение в самой базе. Выбирать за пользователя нельзя.
            if (resolved.Code == DeviceRegistryCodes.DeviceAmbiguous && resolved.Devices.Count > 0)
            {
                RegistryDevice? chosen = await ChooseDeviceAsync(
                    xamlRoot,
                    resolved.Devices,
                    resolved.Message);
                return chosen is null
                    ? new DeviceResolution(null, string.Empty, [])
                    : new DeviceResolution(chosen, string.Empty, []);
            }

            return new DeviceResolution(null, resolved.Message, []);
        }

        // Серийного номера нет: в кадре измерений он не хранится. По сигнатуре
        // известны тип и число передатчиков — по ним и предлагается выбор.
        DeviceRegistryResult<IReadOnlyList<RegistryDevice>> search = await client.SearchDevicesAsync(
            source.DeviceType,
            source.TransmitterCount,
            CancellationToken.None);

        if (!search.Success || search.Value is null)
        {
            return new DeviceResolution(null, search.Message, []);
        }

        if (search.Value.Count == 0)
        {
            return new DeviceResolution(
                null,
                "В базе нет приборов, подходящих под сигнатуру этого файла. " +
                "Зарегистрируйте прибор на сайте или подключите его к приложению.",
                []);
        }

        RegistryDevice? selected = await ChooseDeviceAsync(
            xamlRoot,
            search.Value,
            "Серийный номер в файле данных не хранится. Выберите прибор, которому принадлежит запись.");
        return new DeviceResolution(selected, string.Empty, []);
    }

    private static async Task<RegistryDevice?> ChooseDeviceAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<RegistryDevice> devices,
        string explanation)
    {
        ComboBox selector = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = devices.Select(device => device.DisplayName).ToList(),
            SelectedIndex = 0
        };

        StackPanel panel = new() { Spacing = 12, Width = 460 };
        panel.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(selector);

        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = "Выбор прибора",
            Content = panel,
            PrimaryButtonText = "Выбрать",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        int index = selector.SelectedIndex;
        return index >= 0 && index < devices.Count ? devices[index] : null;
    }

    private sealed record UploadConfirmation(string FileName);

    private static async Task<UploadConfirmation?> ConfirmAsync(
        XamlRoot xamlRoot,
        UploadSource source,
        RegistryDevice device,
        string suggestedName,
        IReadOnlyList<string> warnings)
    {
        TextBox fileNameBox = new()
        {
            Text = suggestedName,
            Header = "Имя файла"
        };

        StackPanel panel = new() { Spacing = 10, Width = 480 };
        panel.Children.Add(CreateInfoLine("Прибор", device.DisplayName));
        panel.Children.Add(CreateInfoLine("Раздел", source.Kind.ToDisplayName()));
        panel.Children.Add(CreateInfoLine("Источник", source.Description));
        panel.Children.Add(CreateInfoLine("Размер", FormatSize(source.Content.Length)));
        panel.Children.Add(fileNameBox);

        foreach (string warning in warnings)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "⚠ " + warning,
                TextWrapping = TextWrapping.Wrap
            });
        }

        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = "Загрузка в базу данных",
            Content = panel,
            PrimaryButtonText = "Загрузить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        while (true)
        {
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            string fileName = fileNameBox.Text.Trim();
            if (UploadFileName.IsAcceptable(fileName))
            {
                return new UploadConfirmation(fileName);
            }

            // Правила совпадают с серверными: лучше сказать об этом здесь,
            // чем получить отказ после передачи файла.
            await ShowMessageAsync(
                xamlRoot,
                "Недопустимое имя файла",
                "Имя может содержать буквы, цифры, пробел и символы . - _ ( ) [ ] " +
                "и должно оканчиваться расширением.");
        }
    }

    private static TextBlock CreateInfoLine(string caption, string value)
    {
        return new TextBlock
        {
            Text = $"{caption}: {value}",
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} Б";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} КБ";
        }

        return $"{bytes / (1024.0 * 1024.0):F1} МБ";
    }

    private static async Task<bool> ConfirmSimpleAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string primaryText)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "ОК"
        };

        await dialog.ShowAsync();
    }
}
