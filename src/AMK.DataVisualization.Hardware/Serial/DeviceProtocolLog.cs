using System.Text;

namespace AMK.DataVisualization.Hardware.Serial;

internal static class DeviceProtocolLog
{
    private const string LogFileName = "CartographLwd4Tx.log";
    private const string SolutionFileName = "AMK-Data-Visualization.slnx";
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string ResolvedLogPath = ResolveLogPath();
    private static long sequence;
    private static string session = "startup";

    public static string LogPath => ResolvedLogPath;

    public static void BeginSession(string portName, int baudRate)
    {
        lock (Sync)
        {
            session = $"{DateTime.Now:yyyyMMdd-HHmmss.fff}-P{Environment.ProcessId}";
            sequence = 0;
            AppendCore(
                $"=== SESSION START | port={portName} | baud={baudRate} | " +
                $"process={Environment.ProcessId} | base={AppContext.BaseDirectory} | cwd={Environment.CurrentDirectory} ===");
        }
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            AppendCore(message);
        }
    }

    public static void Write(Exception exception, string message)
    {
        Write($"{message} | exception={exception.GetType().Name} | message={exception.Message}");
    }

    public static string FormatBytes(ReadOnlySpan<byte> bytes, int headLength = 32, int tailLength = 16)
    {
        if (bytes.IsEmpty)
        {
            return "<empty>";
        }

        if (bytes.Length <= headLength + tailLength)
        {
            return Convert.ToHexString(bytes);
        }

        return $"{Convert.ToHexString(bytes[..headLength])}...{Convert.ToHexString(bytes[^tailLength..])}";
    }

    private static void AppendCore(string message)
    {
        try
        {
            long currentSequence = ++sequence;
            string line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | session={session} | seq={currentSequence:D6} | " +
                $"thread={Environment.CurrentManagedThreadId} | {message}{Environment.NewLine}";
            File.AppendAllText(ResolvedLogPath, line, Utf8WithoutBom);
        }
        catch
        {
            // Диагностика не должна прерывать обмен с физическим прибором.
        }
    }

    private static string ResolveLogPath()
    {
        foreach (string startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(startPath);
            }
            catch
            {
                continue;
            }

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                {
                    return Path.Combine(directory.FullName, LogFileName);
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, LogFileName);
    }
}
