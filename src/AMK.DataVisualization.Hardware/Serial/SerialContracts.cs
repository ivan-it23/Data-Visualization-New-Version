using System.Text.Json.Serialization;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

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

public readonly record struct MetrologyReadResult(
    SerialOperationResult Result,
    MetrologyData Data)
{
    public bool IsSuccess => Result == SerialOperationResult.Success;
}

public sealed record SerialStatusSnapshot(
    SerialConnectionState State,
    string PortName,
    bool ConnectionRequested,
    bool IsPolling,
    int PollIntervalMilliseconds,
    long PacketsReceived,
    long PacketErrors,
    long LastFrameNumber,
    string LastError,
    uint? DeviceSignature,
    DeviceMetrologySummary? Metrology,
    string RecordFilePath,
    long RecordedFrames);

public sealed class SerialFrameReceivedEventArgs(
    ParsedToolFrame frame,
    byte[] rawPayload,
    long sessionFrameNumber) : EventArgs
{
    public ParsedToolFrame Frame { get; } = frame;

    public byte[] RawPayload { get; } = rawPayload;

    public long SessionFrameNumber { get; } = sessionFrameNumber;
}

public sealed class SerialStatusChangedEventArgs(SerialStatusSnapshot status) : EventArgs
{
    public SerialStatusSnapshot Status { get; } = status;
}

public sealed class SerialConnectionSettings
{
    public string LastPort { get; set; } = string.Empty;

    // The poll interval is intentionally not persisted: every session starts at
    // the default and any change made by the user applies to the current
    // session only. JsonIgnore keeps it out of connection-settings.json so a
    // fresh Load always yields the default below.
    [JsonIgnore]
    public int PollIntervalMilliseconds { get; set; } = 2000;

    public bool RecordingEnabled { get; set; } = true;

    public bool AppendToExistingFile { get; set; }

    public bool ShowFullExistingFile { get; set; }

    public string RecordFolder { get; set; } = SerialRecordingFolder.DefaultPath;

    public int RecordFolderSettingsVersion { get; set; }

    public string ExistingRecordFile { get; set; } = string.Empty;
}
