using AMK.DataVisualization.DeviceRegistry;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Состояние связи с базой учёта приборов на время работы приложения.
/// Хранит настройки, выдаёт клиента и остаётся единственным местом, где
/// приложение узнаёт, настроена ли связь.
/// </summary>
internal sealed class DeviceRegistryConnection
{
    private readonly DeviceRegistrySettingsStore store = new();

    public DeviceRegistryConnection()
    {
        Settings = store.Load();
    }

    public DeviceRegistrySettings Settings { get; private set; }

    public bool IsConfigured => Settings.IsConfigured;

    public string SettingsPath => store.SettingsPath;

    public bool Save(string serverUrl, string token)
    {
        DeviceRegistrySettings updated = new()
        {
            ServerUrl = serverUrl,
            Token = token
        };

        if (!store.TrySave(updated))
        {
            return false;
        }

        Settings = updated;
        return true;
    }

    public DeviceRegistryClient CreateClient()
    {
        return new DeviceRegistryClient(Settings);
    }
}
