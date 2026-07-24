using System.Text.Json;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed class SerialConnectionSettingsStore
{
    private const int CurrentRecordFolderSettingsVersion = 1;
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
        SerialConnectionSettings settings = new();
        try
        {
            if (File.Exists(settingsPath))
            {
                SerialConnectionSettings? storedSettings = JsonSerializer.Deserialize<SerialConnectionSettings>(
                    File.ReadAllText(settingsPath),
                    JsonOptions);
                if (storedSettings is not null)
                {
                    settings = storedSettings;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        Prepare(settings);
        return settings;
    }

    public void Save(SerialConnectionSettings settings)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Prepare(settings);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Prepare(SerialConnectionSettings settings)
    {
        if (settings.RecordFolderSettingsVersion < CurrentRecordFolderSettingsVersion)
        {
            if (string.IsNullOrWhiteSpace(settings.RecordFolder)
                || SerialRecordingFolder.IsDesktopDirectory(settings.RecordFolder))
            {
                settings.RecordFolder = SerialRecordingFolder.DefaultPath;
            }

            settings.RecordFolderSettingsVersion = CurrentRecordFolderSettingsVersion;
        }

        settings.RecordFolder = SerialRecordingFolder.Resolve(settings.RecordFolder);
        SerialRecordingFolder.TryEnsureExists(settings.RecordFolder);
    }
}
