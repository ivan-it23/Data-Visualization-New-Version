using AMK.DataVisualization.Core.Adapters;

namespace AMK.DataVisualization.Hardware.Serial;

public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    ConnectionError
}

public enum SerialOperationResult
{
    Success,
    PortNotFound,
    PortBusy,
    ConnectionFailed,
    Timeout,
    CrcError,
    InvalidSignature,
    InvalidResponse,
    NotConnected,
    Cancelled
}

public sealed record DeviceMetrologySummary(
    uint Signature,
    uint SerialNumber,
    uint DiameterMillimeters);

public sealed record SerialStatusSnapshot(
    SerialConnectionState State,
    string PortName,
    bool IsPolling,
    int PollIntervalMilliseconds,
    long PacketsReceived,
    long PacketErrors,
    uint LastFrameNumber,
    string LastError,
    uint? DeviceSignature,
    DeviceMetrologySummary? Metrology,
    string RecordFilePath,
    long RecordedFrames);

public sealed class SerialFrameReceivedEventArgs(ParsedToolFrame frame, byte[] rawPayload) : EventArgs
{
    public ParsedToolFrame Frame { get; } = frame;

    public byte[] RawPayload { get; } = rawPayload;
}

public sealed class SerialStatusChangedEventArgs(SerialStatusSnapshot status) : EventArgs
{
    public SerialStatusSnapshot Status { get; } = status;
}

public sealed class SerialConnectionSettings
{
    public string LastPort { get; set; } = string.Empty;

    public int PollIntervalMilliseconds { get; set; } = 2000;

    public bool RecordingEnabled { get; set; } = true;

    public bool AppendToExistingFile { get; set; }

    public bool ShowFullExistingFile { get; set; }

    public string RecordFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public string ExistingRecordFile { get; set; } = string.Empty;
}
