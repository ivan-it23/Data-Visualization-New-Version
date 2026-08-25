using System.Text.Json;

namespace AMK.DataVisualization.Hardware.Serial;

public sealed class SerialConnectionSettingsStore
{
    private const int CurrentRecordFolderSettingsVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    // Выбор дозаписи живёт здесь, а не в файле настроек: на диске он
    // заставил бы следующий запуск дописывать файл прошлой сессии, а внутри
    // сессии он всё же нужен обоим окнам — окно подключения его сохраняет,
    // главное окно перечитывает перед подключением. Экземпляр хранилища в
    // приложении один, поэтому этих полей достаточно.
    private bool appendToExistingFile;
    private bool showFullExistingFile;
    private string existingRecordFile = string.Empty;

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
        ApplySessionRecordingChoice(settings);
        return settings;
    }

    public void Save(SerialConnectionSettings settings)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        CaptureSessionRecordingChoice(settings);
        Prepare(settings);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>Запоминает выбор дозаписи на время работы приложения.</summary>
    private void CaptureSessionRecordingChoice(SerialConnectionSettings settings)
    {
        appendToExistingFile = settings.AppendToExistingFile;
        showFullExistingFile = settings.ShowFullExistingFile;
        existingRecordFile = settings.ExistingRecordFile ?? string.Empty;
    }

    /// <summary>
    /// Возвращает выбор дозаписи, сделанный в этой сессии. При первом
    /// чтении после запуска поля пустые, поэтому запись идёт в новый файл.
    /// </summary>
    private void ApplySessionRecordingChoice(SerialConnectionSettings settings)
    {
        settings.AppendToExistingFile = appendToExistingFile;
        settings.ShowFullExistingFile = showFullExistingFile;
        settings.ExistingRecordFile = existingRecordFile;
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
