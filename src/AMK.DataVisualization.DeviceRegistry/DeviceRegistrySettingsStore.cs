using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMK.DataVisualization.DeviceRegistry;

/// <summary>
/// Настройки связи с базой учёта приборов.
/// </summary>
public sealed class DeviceRegistrySettings
{
    /// <summary>Адрес сайта учёта приборов.</summary>
    public string ServerUrl { get; set; } = "https://amk-horizon.ru";

    /// <summary>
    /// Токен, зашифрованный DPAPI и записанный в Base64. В открытом виде на
    /// диск не попадает: скопированный на другой компьютер файл настроек
    /// расшифровать нельзя.
    /// </summary>
    public string ProtectedToken { get; set; } = string.Empty;

    /// <summary>Токен в открытом виде — только в памяти.</summary>
    [JsonIgnore]
    public string Token { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);
}

/// <summary>
/// Хранение настроек в профиле пользователя рядом с остальными настройками
/// приложения.
/// </summary>
public sealed class DeviceRegistrySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string settingsPath;

    public DeviceRegistrySettingsStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AMK-Data-Visualization");
        settingsPath = Path.Combine(root, "device-registry-settings.json");
    }

    public string SettingsPath => settingsPath;

    public DeviceRegistrySettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new DeviceRegistrySettings();
            }

            string json = File.ReadAllText(settingsPath, Encoding.UTF8);
            DeviceRegistrySettings settings =
                JsonSerializer.Deserialize<DeviceRegistrySettings>(json, SerializerOptions)
                ?? new DeviceRegistrySettings();

            settings.Token = DataProtection.TryUnprotect(settings.ProtectedToken);
            return settings;
        }
        catch (Exception)
        {
            // Повреждённый файл настроек не должен мешать работе приложения:
            // пользователь просто вводит токен заново.
            return new DeviceRegistrySettings();
        }
    }

    public bool TrySave(DeviceRegistrySettings settings)
    {
        try
        {
            settings.ProtectedToken = DataProtection.Protect(settings.Token);
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                settingsPath,
                JsonSerializer.Serialize(settings, SerializerOptions),
                new UTF8Encoding(false));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Шифрование токена средствами Windows (DPAPI) для текущего пользователя.
/// </summary>
internal static class DataProtection
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        byte[] plain = Encoding.UTF8.GetBytes(value);
        DataBlob input = default;
        DataBlob output = default;
        try
        {
            input = Allocate(plain);
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
            {
                return string.Empty;
            }

            byte[] encrypted = new byte[output.DataLength];
            Marshal.Copy(output.DataPointer, encrypted, 0, output.DataLength);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception)
        {
            return string.Empty;
        }
        finally
        {
            Release(ref input);
            Release(ref output);
        }
    }

    public static string TryUnprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        DataBlob input = default;
        DataBlob output = default;
        try
        {
            input = Allocate(Convert.FromBase64String(protectedValue));
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
            {
                return string.Empty;
            }

            byte[] plain = new byte[output.DataLength];
            Marshal.Copy(output.DataPointer, plain, 0, output.DataLength);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            return string.Empty;
        }
        finally
        {
            Release(ref input);
            Release(ref output);
        }
    }

    private static DataBlob Allocate(byte[] data)
    {
        IntPtr pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob { DataLength = data.Length, DataPointer = pointer };
    }

    private static void Release(ref DataBlob blob)
    {
        if (blob.DataPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.DataPointer);
            blob.DataPointer = IntPtr.Zero;
            blob.DataLength = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        ref DataBlob output);
}
