using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AMK.DataVisualization.Core;

namespace AMK.DataVisualization.DeviceRegistry;

/// <summary>
/// Клиент базы учёта приборов.
///
/// Приложение не обращается к базе напрямую: учётные данные пришлось бы
/// положить внутрь установщика. Весь обмен идёт через API сайта по токену,
/// который пользователь создаёт в своём профиле.
/// </summary>
public sealed class DeviceRegistryClient : IDisposable
{
    private const int DefaultTimeoutSeconds = 30;
    private const int UploadTimeoutSeconds = 180;

    private readonly HttpClient httpClient;
    private readonly string endpointBase;

    public DeviceRegistryClient(DeviceRegistrySettings settings)
    {
        endpointBase = BuildEndpointBase(settings.ServerUrl);
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(UploadTimeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.Token);

        // Часть конфигураций Apache вырезает заголовок Authorization,
        // поэтому дублируем токен в собственном заголовке.
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Token", settings.Token);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AMK-DataVisualization");
    }

    /// <summary>
    /// Приводит введённый пользователем адрес к адресу точки входа API.
    /// Принимаются и «amk-horizon.ru», и «https://amk-horizon.ru/front.php».
    /// Адрес без схемы дополняется https: сайт работает по нему, и токен
    /// не должен уходить открытым текстом.
    /// </summary>
    public static string BuildEndpointBase(string serverUrl)
    {
        string value = (serverUrl ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        value = value.TrimEnd('/');
        if (value.EndsWith("/front.php", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^"/front.php".Length];
        }

        return value + "/front.php";
    }

    public async Task<DeviceRegistryResult<RegistryUser>> WhoAmIAsync(CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new();
        JsonElement? root = await PostAsync("whoami", content, DefaultTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return DeviceRegistryResult<RegistryUser>.Fail(
                DeviceRegistryCodes.NetworkError,
                lastTransportError);
        }

        if (!IsSuccess(root.Value))
        {
            return DeviceRegistryResult<RegistryUser>.Fail(ReadCode(root.Value), ReadMessage(root.Value));
        }

        JsonElement user = root.Value.GetProperty("user");
        return DeviceRegistryResult<RegistryUser>.Ok(new RegistryUser(
            ReadString(user, "username"),
            ReadString(user, "full_name")));
    }

    /// <summary>
    /// Определяет прибор по серийному номеру из сигнатуры (поле «Серийный №»
    /// окна метрологии). Номер корпуса карточки берётся только из ответа
    /// сервера.
    /// </summary>
    public async Task<DeviceRegistryResult<RegistryDevice>> ResolveDeviceAsync(
        int serialNumber,
        DeviceType deviceType,
        int transmitterCount,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new()
        {
            { new StringContent(serialNumber.ToString(CultureInfo.InvariantCulture)), "serial_number" },
            { new StringContent(DeviceTypeCodes.ToServerCode(deviceType)), "device_type" },
            { new StringContent(transmitterCount.ToString(CultureInfo.InvariantCulture)), "transmitter_count" }
        };

        JsonElement? root = await PostAsync("deviceResolve", content, DefaultTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return DeviceRegistryResult<RegistryDevice>.Fail(
                DeviceRegistryCodes.NetworkError,
                lastTransportError);
        }

        if (!IsSuccess(root.Value))
        {
            return DeviceRegistryResult<RegistryDevice>.Fail(
                ReadCode(root.Value),
                ReadMessage(root.Value),
                ReadDevices(root.Value, "devices"));
        }

        return DeviceRegistryResult<RegistryDevice>.Ok(
            ReadDevice(root.Value.GetProperty("device")),
            ReadStrings(root.Value, "warnings"));
    }

    /// <summary>
    /// Список приборов по типу и числу передатчиков — для файлов данных,
    /// в которых серийного номера нет.
    /// </summary>
    public async Task<DeviceRegistryResult<IReadOnlyList<RegistryDevice>>> SearchDevicesAsync(
        DeviceType deviceType,
        int transmitterCount,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new()
        {
            { new StringContent(DeviceTypeCodes.ToServerCode(deviceType)), "device_type" },
            { new StringContent(transmitterCount.ToString(CultureInfo.InvariantCulture)), "transmitter_count" }
        };

        JsonElement? root = await PostAsync("deviceSearch", content, DefaultTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return DeviceRegistryResult<IReadOnlyList<RegistryDevice>>.Fail(
                DeviceRegistryCodes.NetworkError,
                lastTransportError);
        }

        if (!IsSuccess(root.Value))
        {
            return DeviceRegistryResult<IReadOnlyList<RegistryDevice>>.Fail(
                ReadCode(root.Value),
                ReadMessage(root.Value));
        }

        return DeviceRegistryResult<IReadOnlyList<RegistryDevice>>.Ok(ReadDevices(root.Value, "devices"));
    }

    public async Task<DeviceRegistryResult<RegistryFile>> UploadAsync(
        UploadRequest request,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new()
        {
            { new StringContent(request.Kind.ToFieldName()), "field_name" },
            { new StringContent(request.Device.SerialNumber), "serial_number" },
            { new StringContent(request.Device.CaseNumber), "case_number" },
            { new StringContent(request.Device.DeviceType), "device_type" },
            { new StringContent(request.FileName), "file_name" },
            { new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Content)).ToLowerInvariant()), "sha256" }
        };

        if (request.Force)
        {
            content.Add(new StringContent("1"), "force");
        }

        ByteArrayContent fileContent = new(request.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", request.FileName);

        JsonElement? root = await PostAsync("fileUpload", content, UploadTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return DeviceRegistryResult<RegistryFile>.Fail(
                DeviceRegistryCodes.NetworkError,
                lastTransportError);
        }

        if (!IsSuccess(root.Value))
        {
            RegistryFile? existing = null;
            if (root.Value.TryGetProperty("existing", out JsonElement existingElement)
                && existingElement.ValueKind == JsonValueKind.Object)
            {
                existing = ReadFile(existingElement);
            }

            return DeviceRegistryResult<RegistryFile>.Fail(
                ReadCode(root.Value),
                ReadMessage(root.Value),
                existingFile: existing);
        }

        return DeviceRegistryResult<RegistryFile>.Ok(ReadFile(root.Value.GetProperty("file")));
    }

    public async Task<DeviceRegistryResult<IReadOnlyList<RegistryFile>>> ListFilesAsync(
        UploadKind kind,
        RegistryDevice device,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new()
        {
            { new StringContent(kind.ToFieldName()), "field_name" },
            { new StringContent(device.SerialNumber), "serial_number" },
            { new StringContent(device.CaseNumber), "case_number" },
            { new StringContent(device.DeviceType), "device_type" }
        };

        JsonElement? root = await PostAsync("fileList", content, DefaultTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return DeviceRegistryResult<IReadOnlyList<RegistryFile>>.Fail(
                DeviceRegistryCodes.NetworkError,
                lastTransportError);
        }

        if (!IsSuccess(root.Value))
        {
            return DeviceRegistryResult<IReadOnlyList<RegistryFile>>.Fail(
                ReadCode(root.Value),
                ReadMessage(root.Value));
        }

        List<RegistryFile> files = [];
        if (root.Value.TryGetProperty("files", out JsonElement filesElement)
            && filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in filesElement.EnumerateArray())
            {
                files.Add(ReadFile(item));
            }
        }

        return DeviceRegistryResult<IReadOnlyList<RegistryFile>>.Ok(files);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    // ------------------------------------------------------------------

    private string lastTransportError = string.Empty;

    private async Task<JsonElement?> PostAsync(
        string action,
        MultipartFormDataContent content,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        lastTransportError = string.Empty;
        if (endpointBase.Length == 0)
        {
            lastTransportError = "Не задан адрес сервера базы приборов.";
            return null;
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        string requestUri = $"{endpointBase}?c=Api&a={action}";
        try
        {
            using HttpResponseMessage response = await httpClient
                .PostAsync(requestUri, content, timeout.Token)
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Сервер ответил не JSON: чаще всего это страница входа или
                // сообщение об ошибке PHP. Показываем начало ответа —
                // по нему сразу видно, что именно пришло.
                lastTransportError = BuildNonJsonMessage(response, body);
                return null;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lastTransportError = "Сервер не ответил за отведённое время.";
            return null;
        }
        catch (HttpRequestException exception)
        {
            lastTransportError = $"Не удалось связаться с сервером: {exception.Message}";
            return null;
        }
    }

    private static string BuildNonJsonMessage(HttpResponseMessage response, string body)
    {
        string preview = body.Length > 200 ? body[..200] + "…" : body;
        preview = preview.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return $"Сервер вернул неожиданный ответ (код {(int)response.StatusCode}): {preview}";
    }

    private static bool IsSuccess(JsonElement root)
    {
        return root.TryGetProperty("success", out JsonElement success)
            && success.ValueKind == JsonValueKind.True;
    }

    private static string ReadCode(JsonElement root)
    {
        return ReadString(root, "code");
    }

    private static string ReadMessage(JsonElement root)
    {
        string message = ReadString(root, "message");
        return message.Length > 0 ? message : "Сервер отклонил запрос без пояснения.";
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long number) ? number : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0,
            _ => 0
        };
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values;
    }

    private static IReadOnlyList<RegistryDevice> ReadDevices(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RegistryDevice> devices = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            devices.Add(ReadDevice(item));
        }

        return devices;
    }

    private static RegistryDevice ReadDevice(JsonElement element)
    {
        return new RegistryDevice(
            (int)ReadInt64(element, "id"),
            ReadString(element, "device_type"),
            ReadString(element, "serial_number"),
            ReadString(element, "case_number"),
            ReadString(element, "nominal_diameter"),
            ReadString(element, "transmitter_count"));
    }

    private static RegistryFile ReadFile(JsonElement element)
    {
        DateTimeOffset? createdAt = null;
        string rawDate = ReadString(element, "created_at");
        if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            createdAt = new DateTimeOffset(parsed, TimeSpan.Zero);
        }

        return new RegistryFile(
            (int)ReadInt64(element, "id"),
            ReadString(element, "file_name"),
            createdAt,
            ReadInt64(element, "size"),
            ReadString(element, "sha256"));
    }
}
