using System.Text;

namespace AMK.DataVisualization.DeviceRegistry;

/// <summary>
/// Единый шаблон имени загружаемого файла.
///
/// Имена в базе исторически произвольные («Dataout_cartograf.bin»,
/// «IndRAM.DEV»), и найти по ним нужную запись нельзя. Приложение знает тип
/// прибора, серийный номер, корпус и время, поэтому предлагает имя, по которому
/// файл опознаётся без открытия.
/// </summary>
public static class UploadFileName
{
    /// <summary>Символы, которые принимает сервер, плюс запас на разделители.</summary>
    private const string AllowedPunctuation = " .-_()[]";

    public static string Build(
        UploadKind kind,
        RegistryDevice device,
        DateTimeOffset timestamp,
        string extension)
    {
        string normalizedExtension = NormalizeExtension(extension);
        string deviceType = Sanitize(device.DeviceType);
        string serial = Sanitize(device.SerialNumber);
        string caseNumber = Sanitize(device.CaseNumber);

        StringBuilder builder = new();
        builder.Append(kind.ToFileNamePrefix());
        if (deviceType.Length > 0)
        {
            builder.Append('_').Append(deviceType);
        }

        if (serial.Length > 0)
        {
            builder.Append('_').Append(serial);
        }

        if (caseNumber.Length > 0)
        {
            builder.Append('(').Append(caseNumber).Append(')');
        }

        builder.Append('_').Append(timestamp.ToString("yyyyMMdd_HHmmss"));
        builder.Append(normalizedExtension);
        return builder.ToString();
    }

    /// <summary>
    /// Проверка имени по тем же правилам, что применяет сервер: пользователь
    /// может отредактировать предложенное имя.
    /// </summary>
    public static bool IsAcceptable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
        {
            return false;
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return false;
        }

        foreach (char character in fileName)
        {
            if (!char.IsLetterOrDigit(character) && !AllowedPunctuation.Contains(character))
            {
                return false;
            }
        }

        return Path.GetExtension(fileName).Length > 1;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
            else if (character is ' ' or '.')
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }
}
