namespace AMK.DataVisualization.Hardware.Serial;

internal static class SerialRecordingFolder
{
    private const string DefaultFolderName = "AMK_Data";

    public static string DefaultPath
    {
        get
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                desktop = string.IsNullOrWhiteSpace(userProfile)
                    ? Environment.CurrentDirectory
                    : Path.Combine(userProfile, "Desktop");
            }

            return Path.Combine(desktop, DefaultFolderName);
        }
    }

    public static string Resolve(string? configuredPath)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultPath
            : configuredPath.Trim();
    }

    public static bool IsDesktopDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
        {
            return false;
        }

        try
        {
            string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string desktopDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(desktop));
            return string.Equals(candidate, desktopDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static void TryEnsureExists(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            // Keep the configured path; OpenRecordStreamCore retries authoritative creation before writing.
        }
    }
}
