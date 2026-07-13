using System.Text.Json;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed class SerialConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public SerialConnectionSettingsStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AMK-Data-Visualization");
        settingsPath = Path.Combine(root, "connection-settings.json");
    }

    public SerialConnectionSettings Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                SerialConnectionSettings? settings = JsonSerializer.Deserialize<SerialConnectionSettings>(
                    File.ReadAllText(settingsPath),
                    JsonOptions);
                if (settings is not null)
                {
                    settings.PollIntervalMilliseconds = Math.Clamp(settings.PollIntervalMilliseconds, 500, 30000);
                    if (string.IsNullOrWhiteSpace(settings.RecordFolder))
                    {
                        settings.RecordFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    }

                    return settings;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new SerialConnectionSettings();
    }

    public void Save(SerialConnectionSettings settings)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.PollIntervalMilliseconds = Math.Clamp(settings.PollIntervalMilliseconds, 500, 30000);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
