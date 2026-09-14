using System.Globalization;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Metrology;

/// <summary>
/// Отметка времени записи метрологии: постановка при записи и разбор при
/// показе.
/// </summary>
/// <remarks>
/// Поле появилось только в новой прошивке и занимает восемь байт со смещения
/// 124, отобранные у служебной области. У старой прошивки по этим смещениям
/// лежит её собственная служебная область, смысл которой приложению
/// неизвестен, поэтому постановка отметки закрыта поколением источника — тем
/// же правилом, по которому не пишутся воздушные поправки затухания.
///
/// Хранится всегда в UTC: <c>time()</c> прошивки и коллеги по проекту
/// возвращают секунды Unix-эпохи, и пояс компьютера в них не входит.
/// Пользователю значение показывается в местном времени.
/// </remarks>
public static class MetrologyTimestamp
{
    /// <summary>Формат, в котором отметка выводится пользователю.</summary>
    public const string DisplayFormat = "dd.MM.yyyy HH:mm:ss";

    /// <summary>
    /// Нижняя граница правдоподобной отметки. Всё, что раньше, отметкой не
    /// считается: неразмеченная память прибора даёт восемь байт <c>0xFF</c>,
    /// то есть −1, а метрология, записанная до появления поля, — нули.
    /// </summary>
    private static readonly long MinimumValidSeconds =
        new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>
    /// Верхняя граница правдоподобной отметки. Защищает от чужих байт,
    /// случайно попавших на это смещение.
    /// </summary>
    private static readonly long MaximumValidSeconds =
        new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>
    /// Ставит отметку времени записи. Для старой прошивки не делает ничего и
    /// возвращает <c>false</c>: её служебную область трогать нельзя.
    /// </summary>
    public static bool TryStamp(ref MetrologyData data, FirmwareGeneration generation, DateTimeOffset moment)
    {
        if (generation != FirmwareGeneration.Extended)
        {
            return false;
        }

        data.TimestampUnixSeconds = moment.ToUnixTimeSeconds();
        return true;
    }

    /// <summary>
    /// Ставит отметку текущего времени компьютера.
    /// </summary>
    public static bool TryStamp(ref MetrologyData data, FirmwareGeneration generation)
    {
        return TryStamp(ref data, generation, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Отметка в местном времени. <c>false</c> означает, что даты в метрологии
    /// нет: поле не заполнялось либо содержит не дату.
    /// </summary>
    public static bool TryGetLocal(long unixSeconds, out DateTimeOffset local)
    {
        local = default;
        if (unixSeconds < MinimumValidSeconds || unixSeconds > MaximumValidSeconds)
        {
            return false;
        }

        local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        return true;
    }

    /// <summary>
    /// Отметка в виде, пригодном для показа. Пустая строка означает, что даты
    /// в метрологии нет; поле окна в этом случае остаётся пустым, как и
    /// незаданная воздушная поправка затухания.
    /// </summary>
    public static string Describe(long unixSeconds)
    {
        return TryGetLocal(unixSeconds, out DateTimeOffset local)
            ? local.ToString(DisplayFormat, CultureInfo.CurrentCulture)
            : string.Empty;
    }
}
