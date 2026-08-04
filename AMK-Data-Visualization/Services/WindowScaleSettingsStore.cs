using System.Text.Json;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Loads and saves <see cref="WindowScaleSettings"/> in the same local
/// application-data folder as the other user settings, in a dedicated file so
/// unrelated concerns stay separated.
/// </summary>
public sealed class WindowScaleSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public WindowScaleSettingsStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AMK-Data-Visualization");
        settingsPath = Path.Combine(root, "window-scale-settings.json");
    }

    public WindowScaleSettings Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                WindowScaleSettings? stored = JsonSerializer.Deserialize<WindowScaleSettings>(
                    File.ReadAllText(settingsPath),
                    JsonOptions);
                if (stored is not null)
                {
                    return stored;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new WindowScaleSettings();
    }

    public void Save(WindowScaleSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
