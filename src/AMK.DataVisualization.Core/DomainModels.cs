namespace AMK.DataVisualization.Core;

public enum FileFormat
{
    Bin,
    Biv,
    Dev,
    Unknown
}

public enum DeviceType
{
    Unknown = 0,
    Autonom = 1,
    Lwd = 2
}

public enum ToolDataKind
{
    Unknown,
    Autonom,
    Lwd
}

public sealed record ToolCapabilities(
    ToolDataKind Kind,
    int TransmitterCount,
    bool HasT5,
    bool HasSeparatePhaseRx,
    bool HasDeltaPhase,
    bool HasZeroAmplitudeRx1,
    bool HasZeroAmplitudeRx2,
    bool HasZeroDeltaPhase,
    bool HasSondeRhoSmt,
    bool HasSondePhaseSmt,
    bool SupportsBinContainer,
    bool SupportsBivContainer,
    bool SupportsDevContainer,
    bool SupportsComPolling,
    bool SupportsSilentData)
{
    public static ToolCapabilities Empty { get; } = new(
        ToolDataKind.Unknown,
        0,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false);
}

public sealed record ToolRecordMetadata(ToolDataKind Kind, ToolCapabilities Capabilities)
{
    public static ToolRecordMetadata Empty { get; } = new(ToolDataKind.Unknown, ToolCapabilities.Empty);
}

public readonly record struct ToolIdentity(
    uint Signature,
    uint ToolCode,
    DeviceType DeviceType,
    int TransmitterCount,
    int Modification,
    ToolDataKind Kind,
    string DisplayName);
