using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;

namespace AMK.DataVisualization.App.Services;

internal sealed record UpdateCheckResult(
    bool IsAvailable,
    Version CurrentVersion,
    Version RemoteVersion,
    string InstallerFileName,
    Uri? DownloadUri,
    string? Sha256,
    string ErrorMessage)
{
    public bool IsSuccessful => string.IsNullOrEmpty(ErrorMessage);
}

internal sealed class AutoUpdateService : IDisposable
{
    private static readonly Uri UpdateRoot = new(
        "https://ivanbohg.beget.tech/FilesForProgramVisualisation/DataVisualization/",
        UriKind.Absolute);
    private static readonly Uri UpdateDescriptorUri = new(
        UpdateRoot,
        "data-visualization-version.txt");
    private const long MaximumInstallerSize = 1024L * 1024L * 1024L;

    private readonly HttpClient httpClient;

    public AutoUpdateService()
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DataVisualizationUpdater", "1.0"));
    }

    public Version CurrentVersion => GetCurrentVersion();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        Version currentVersion = CurrentVersion;

        try
        {
            string body = await httpClient.GetStringAsync(UpdateDescriptorUri, cancellationToken);
            string[] lines = body
                .TrimStart('\uFEFF')
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2 || !TryParseVersion(lines[0], out Version remoteVersion))
            {
                return Failed(
                    currentVersion,
                    "Сервер обновлений вернул некорректный data-visualization-version.txt.");
            }

            string installerFileName = lines[1];
            if (!IsSafeInstallerFileName(installerFileName))
            {
                return Failed(currentVersion, "Сервер обновлений вернул недопустимое имя установщика.");
            }

            string? sha256 = lines.Length >= 3 ? NormalizeSha256(lines[2]) : null;
            if (lines.Length >= 3 && sha256 is null)
            {
                return Failed(currentVersion, "Сервер обновлений вернул некорректную контрольную сумму SHA-256.");
            }

            Version normalizedRemote = NormalizeVersion(remoteVersion);
            return new UpdateCheckResult(
                normalizedRemote > currentVersion,
                currentVersion,
                normalizedRemote,
                installerFileName,
                new Uri(UpdateRoot, Uri.EscapeDataString(installerFileName)),
                sha256,
                string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(currentVersion, "Сервер обновлений не ответил за отведённое время.");
        }
        catch (HttpRequestException exception)
        {
            return Failed(currentVersion, $"Не удалось подключиться к серверу обновлений: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Failed(currentVersion, $"Не удалось прочитать данные обновления: {exception.Message}");
        }
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken)
    {
        if (!update.IsAvailable || update.DownloadUri is null || !IsSafeInstallerFileName(update.InstallerFileName))
        {
            throw new InvalidOperationException("Данные обновления не содержат допустимый установщик.");
        }

        string updateDirectory = Path.Combine(Path.GetTempPath(), "AMKDataVisualization", "Updates");
        Directory.CreateDirectory(updateDirectory);
        DeleteStaleDownloads(updateDirectory);

        string finalPath = Path.Combine(updateDirectory, update.InstallerFileName);
        string temporaryPath = finalPath + ".download";
        File.Delete(temporaryPath);

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is <= 1024 or > MaximumInstallerSize)
            {
                throw new InvalidDataException("Размер установщика обновления недопустим.");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream target = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithLimitAsync(source, target, MaximumInstallerSize, cancellationToken);
            }

            ValidatePortableExecutable(temporaryPath);
            if (update.Sha256 is not null)
            {
                await ValidateSha256Async(temporaryPath, update.Sha256, cancellationToken);
            }

            File.Move(temporaryPath, finalPath, true);
            TryDeleteZoneIdentifier(finalPath);
            return finalPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public static bool StartInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            return false;
        }

        Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /SUPPRESSMSGBOXES /UPDATE=1",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        });
        return process is not null;
    }

    public static string FormatVersion(Version version)
    {
        return $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static UpdateCheckResult Failed(Version currentVersion, string message)
    {
        return new UpdateCheckResult(
            false,
            currentVersion,
            new Version(0, 0, 0),
            string.Empty,
            null,
            null,
            message);
    }

    private static Version GetCurrentVersion()
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? typeof(AutoUpdateService).Assembly;
        string? informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0];
        if (TryParseVersion(informationalVersion, out Version parsed))
        {
            return NormalizeVersion(parsed);
        }

        return NormalizeVersion(entryAssembly.GetName().Version ?? new Version(0, 0, 0));
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        string normalized = value?.Trim().TrimStart('v', 'V') ?? string.Empty;
        if (Version.TryParse(normalized, out Version? parsed) && parsed.Major >= 0)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    private static bool IsSafeInstallerFileName(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static string? NormalizeSha256(string value)
    {
        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream target,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                throw new InvalidDataException("Установщик обновления превышает допустимый размер.");
            }

            await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static void ValidatePortableExecutable(string path)
    {
        FileInfo file = new(path);
        if (file.Length <= 1024)
        {
            throw new InvalidDataException("Скачанный установщик пуст или повреждён.");
        }

        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
        {
            throw new InvalidDataException("Скачанный файл не является Windows-установщиком.");
        }
    }

    private static async Task ValidateSha256Async(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!string.Equals(Convert.ToHexString(actualHash), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Контрольная сумма обновления не совпадает с опубликованной.");
        }
    }

    private static void DeleteStaleDownloads(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteZoneIdentifier(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
