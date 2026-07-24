using System.Text.Json;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Loads and saves <see cref="ChartDisplaySettings"/> in the same local
/// application-data folder as the serial connection settings, in a dedicated
/// file so unrelated concerns stay separated.
/// </summary>
public sealed class ChartDisplaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public ChartDisplaySettingsStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AMK-Data-Visualization");
        settingsPath = Path.Combine(root, "chart-display-settings.json");
    }

    public ChartDisplaySettings Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                ChartDisplaySettings? stored = JsonSerializer.Deserialize<ChartDisplaySettings>(
                    File.ReadAllText(settingsPath),
                    JsonOptions);
                if (stored is not null)
                {
                    stored.Graph1 ??= new GraphDisplayOptions();
                    stored.Graph2 ??= new GraphDisplayOptions();
                    return stored;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new ChartDisplaySettings();
    }

    public void Save(ChartDisplaySettings settings)
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
