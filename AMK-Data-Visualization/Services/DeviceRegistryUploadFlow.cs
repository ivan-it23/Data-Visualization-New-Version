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
/// Серийный номер прибора — поле «Серийный №» окна метрологии, оно же
/// `serial_number` карточки в базе. Кодируется в сигнатуре, поэтому известен и
/// для файла данных, открытого без прибора.
/// </param>
/// <param name="DeviceType">Тип прибора из сигнатуры.</param>
/// <param name="TransmitterCount">Число передатчиков из сигнатуры.</param>
/// <param name="Description">Строка описания источника для окна подтверждения.</param>
internal sealed record UploadSource(
    UploadKind Kind,
    byte[] Content,
    string Extension,
    int? SerialNumber,
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
        if (source.SerialNumber is int serial)
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
                    resolved.Message,
                    string.Empty);
                return new DeviceResolution(chosen, string.Empty, []);
            }

            if (resolved.Code == DeviceRegistryCodes.DeviceNotFound)
            {
                return await OfferSameTypeDevicesAsync(xamlRoot, client, source, serial);
            }

            return new DeviceResolution(null, resolved.Message, []);
        }

        return await OfferSameTypeDevicesAsync(xamlRoot, client, source, serialNumber: null);
    }

    /// <summary>
    /// Прибора с таким номером в базе нет — показываем все приборы того же типа
    /// и с тем же числом передатчиков.
    ///
    /// Чаще всего это означает, что карточку прибора просто не завели или
    /// завели с другим номером, поэтому запрет загрузки здесь был бы тупиком:
    /// пользователь видит реальный список и либо выбирает нужный прибор, либо
    /// идёт создавать карточку по ссылке.
    /// </summary>
    private async Task<DeviceResolution> OfferSameTypeDevicesAsync(
        XamlRoot xamlRoot,
        DeviceRegistryClient client,
        UploadSource source,
        int? serialNumber)
    {
        DeviceRegistryResult<IReadOnlyList<RegistryDevice>> search = await client.SearchDevicesAsync(
            source.DeviceType,
            source.TransmitterCount,
            CancellationToken.None);

        if (!search.Success || search.Value is null)
        {
            return new DeviceResolution(null, search.Message, []);
        }

        string typeName = DeviceTypeCodes.ToDisplayName(source.DeviceType);
        string missing = serialNumber is int number
            ? $"Прибор с серийным номером {number} отсутствует в базе данных. "
            : "Серийный номер прибора определить не удалось. ";

        if (search.Value.Count == 0)
        {
            return new DeviceResolution(
                null,
                missing +
                $"Приборов типа {typeName} с числом передатчиков {source.TransmitterCount} " +
                "в базе тоже нет. Создайте карточку прибора на сайте учёта приборов.",
                []);
        }

        string explanation =
            missing +
            "Возможно, была проблема с созданием карточки прибора в базе. " +
            $"Ниже представлены все существующие приборы типа {typeName} " +
            $"с количеством передатчиков {source.TransmitterCount}. " +
            "Если в списке нет подходящего прибора — создайте карточку прибора на сайте:";

        RegistryDevice? selected = await ChooseDeviceAsync(
            xamlRoot,
            search.Value,
            explanation,
            connection.Settings.ServerUrl);
        return new DeviceResolution(selected, string.Empty, []);
    }

    private static async Task<RegistryDevice?> ChooseDeviceAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<RegistryDevice> devices,
        string explanation,
        string siteUrl)
    {
        ComboBox selector = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = devices.Select(device => device.DisplayName).ToList(),
            SelectedIndex = 0
        };

        StackPanel panel = new() { Spacing = 12, Width = 480 };
        panel.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap });

        if (siteUrl.Length > 0 && Uri.TryCreate(siteUrl, UriKind.Absolute, out Uri? siteUri))
        {
            panel.Children.Add(new HyperlinkButton
            {
                Content = siteUrl,
                NavigateUri = siteUri,
                Padding = new Thickness(0)
            });
        }

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
