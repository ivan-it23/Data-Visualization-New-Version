using AMK.DataVisualization.Core;

namespace AMK.DataVisualization.DeviceRegistry;

/// <summary>
/// Раздел карточки прибора на сайте учёта. Значения совпадают с полями формы
/// загрузки, поэтому переименовывать их нельзя без изменения серверной части.
/// </summary>
public enum UploadKind
{
    /// <summary>Термопрофиль и подвес на воздухе.</summary>
    Thermoprofile,

    /// <summary>Текущие параметры прибора.</summary>
    Metrology,

    /// <summary>Параметры прибора после автокалибровки нулей воздуха.</summary>
    Calibration,

    /// <summary>Контрольно-испытательная скважина.</summary>
    Kis
}

public static class UploadKindExtensions
{
    public static string ToFieldName(this UploadKind kind)
    {
        return kind switch
        {
            UploadKind.Thermoprofile => "thermoprofile",
            UploadKind.Metrology => "metrology",
            UploadKind.Calibration => "calibration",
            UploadKind.Kis => "kis",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static string ToDisplayName(this UploadKind kind)
    {
        return kind switch
        {
            UploadKind.Thermoprofile => "Термопрофиль + Воздух",
            UploadKind.Metrology => "Метрология",
            UploadKind.Calibration => "Калибровка",
            UploadKind.Kis => "КИС",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    /// Префикс имени файла. Имена в базе исторически произвольные, поэтому
    /// приложение предлагает единый шаблон.
    /// </summary>
    public static string ToFileNamePrefix(this UploadKind kind)
    {
        return kind switch
        {
            UploadKind.Thermoprofile => "thermo",
            UploadKind.Metrology => "metro",
            UploadKind.Calibration => "calib",
            UploadKind.Kis => "kis",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

/// <summary>Учётная запись, которой принадлежит токен.</summary>
public sealed record RegistryUser(string UserName, string FullName);

/// <summary>Прибор в базе учёта.</summary>
public sealed record RegistryDevice(
    int Id,
    string DeviceType,
    string SerialNumber,
    string CaseNumber,
    string NominalDiameter,
    string TransmitterCount)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CaseNumber)
        ? $"{DeviceType} № {SerialNumber}"
        : $"{DeviceType} № {SerialNumber} (корпус {CaseNumber})";
}

/// <summary>Загруженный или ранее загруженный файл.</summary>
public sealed record RegistryFile(
    int Id,
    string FileName,
    DateTimeOffset? CreatedAt,
    long Size,
    string Sha256);

/// <summary>
/// Коды, на которые приложение реагирует по-разному. Остальные ответы
/// показываются пользователю текстом сообщения от сервера.
/// </summary>
public static class DeviceRegistryCodes
{
    public const string Unauthorized = "unauthorized";
    public const string DeviceNotFound = "device_not_found";
    public const string DeviceAmbiguous = "device_ambiguous";
    public const string Duplicate = "duplicate";
    public const string RateLimited = "rate_limited";
    public const string FileTooLarge = "file_too_large";
    public const string NotConfigured = "not_configured";
    public const string NetworkError = "network_error";
}

/// <summary>Результат обращения к серверу.</summary>
public sealed class DeviceRegistryResult<T>
{
    private DeviceRegistryResult(
        bool success,
        T? value,
        string code,
        string message,
        IReadOnlyList<string> warnings,
        IReadOnlyList<RegistryDevice> devices,
        RegistryFile? existingFile)
    {
        Success = success;
        Value = value;
        Code = code;
        Message = message;
        Warnings = warnings;
        Devices = devices;
        ExistingFile = existingFile;
    }

    public bool Success { get; }

    public T? Value { get; }

    /// <summary>Машиночитаемый код ошибки; пусто при успехе.</summary>
    public string Code { get; }

    /// <summary>Текст для пользователя.</summary>
    public string Message { get; }

    /// <summary>Расхождения между прибором и карточкой в базе.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Кандидаты, когда серийный номер не определяет прибор однозначно.</summary>
    public IReadOnlyList<RegistryDevice> Devices { get; }

    /// <summary>Ранее загруженный файл с тем же содержимым.</summary>
    public RegistryFile? ExistingFile { get; }

    public static DeviceRegistryResult<T> Ok(T value, IReadOnlyList<string>? warnings = null)
    {
        return new DeviceRegistryResult<T>(true, value, string.Empty, string.Empty, warnings ?? [], [], null);
    }

    public static DeviceRegistryResult<T> Fail(
        string code,
        string message,
        IReadOnlyList<RegistryDevice>? devices = null,
        RegistryFile? existingFile = null)
    {
        return new DeviceRegistryResult<T>(false, default, code, message, [], devices ?? [], existingFile);
    }
}

/// <summary>Запрос на загрузку файла.</summary>
public sealed record UploadRequest(
    UploadKind Kind,
    RegistryDevice Device,
    string FileName,
    byte[] Content)
{
    /// <summary>
    /// Повторная отправка файла, который уже есть в базе. Устанавливается
    /// только после явного подтверждения пользователем.
    /// </summary>
    public bool Force { get; init; }
}

/// <summary>
/// Соответствие типа прибора коду, который понимает сервер.
/// Картограф в canonical-слое помечен как LWD-совместимый, поэтому тип
/// обязан браться из <see cref="DeviceType"/>, а не из вида данных.
/// </summary>
public static class DeviceTypeCodes
{
    public static string ToServerCode(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Autonom => "Autonom",
            DeviceType.Lwd => "Lwd",
            DeviceType.Cartograph => "Cartograph",
            _ => string.Empty
        };
    }
}
