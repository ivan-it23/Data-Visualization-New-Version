namespace AMK.DataVisualization.Hardware.Serial;

internal static class DeviceProtocolLog
{
    private static long sequence;
    private static string session = "startup";

    public static string LogPath => DeviceExchangeLog.FilePath;

    public static void BeginSession(string portName, int baudRate)
    {
        session = $"{DateTime.Now:yyyyMMdd-HHmmss.fff}-P{Environment.ProcessId}";
        Interlocked.Exchange(ref sequence, 0);
        DeviceExchangeLog.SetRawDataAvailable(true);
        DeviceExchangeLog.OpenFile();
        Write(
            $"=== SESSION START | port={portName} | baud={baudRate} | " +
            $"process={Environment.ProcessId} | base={AppContext.BaseDirectory} | cwd={Environment.CurrentDirectory} ===");
    }

    public static void Write(string message)
    {
        long currentSequence = Interlocked.Increment(ref sequence);
        DeviceExchangeLog.AppendRaw(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | session={session} | seq={currentSequence:D6} | " +
            $"thread={Environment.CurrentManagedThreadId} | {message}");
    }

    public static void Write(Exception exception, string message)
    {
        Write($"{message} | exception={exception.GetType().Name} | message={exception.Message}");
    }

    public static string FormatBytes(ReadOnlySpan<byte> bytes, int headLength = 0, int tailLength = 0)
    {
        if (bytes.IsEmpty)
        {
            return "<empty>";
        }

        if (headLength <= 0 || tailLength <= 0 || bytes.Length <= headLength + tailLength)
        {
            return Convert.ToHexString(bytes);
        }

        return $"{Convert.ToHexString(bytes[..headLength])}...{Convert.ToHexString(bytes[^tailLength..])}";
    }
}
