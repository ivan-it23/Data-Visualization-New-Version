using System.Text;

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
        DeviceExchangeLog.BeginDeviceSession(portName, baudRate);
        Write(
            $"=== SESSION START | port={portName} | baud={baudRate} | " +
            $"process={Environment.ProcessId} | base={AppContext.BaseDirectory} | cwd={Environment.CurrentDirectory} ===");
    }

    public static void Write(string message)
    {
        long currentSequence = Interlocked.Increment(ref sequence);
        DeviceExchangeLog.AppendFileOnly(
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

    public static string FormatRawPacket(string label, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return $"{label} (0 B)";
        }

        StringBuilder builder = new(label.Length + 24 + bytes.Length * 3);
        builder.Append(label).Append(" (").Append(bytes.Length).Append(" B): ");
        if (bytes.Length <= 32)
        {
            AppendHexBytes(builder, bytes);
            return builder.ToString();
        }

        builder.Append("\r\n");
        const int bytesPerLine = 16;
        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            builder.Append("  ").Append((offset & 0xFFFF).ToString("X4")).Append(": ");
            AppendHexBytes(builder, bytes.Slice(offset, Math.Min(bytesPerLine, bytes.Length - offset)));
            if (offset + bytesPerLine < bytes.Length)
            {
                builder.Append("\r\n");
            }
        }

        return builder.ToString();
    }

    private static void AppendHexBytes(StringBuilder builder, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            builder.Append(value.ToString("X2")).Append(' ');
        }
    }
}
