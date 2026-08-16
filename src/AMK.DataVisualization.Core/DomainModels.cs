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
    Lwd = 2,
    Cartograph = 3
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
    float AmplitudeScale,
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
        1.0f,
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

/// <param name="Number">
/// Серийный номер прибора, закодированный в сигнатуре, — то же значение, что
/// показывает поле «Серийный №» окна метрологии. Совпадает с полем
/// `serial_number` карточки прибора в базе учёта.
///
/// Не путать с полем `Serial` структуры метрологии: там хранится номер корпуса
/// (поле «Корпус №» того же окна).
/// </param>
public readonly record struct ToolIdentity(
    uint Signature,
    uint ToolCode,
    DeviceType DeviceType,
    int TransmitterCount,
    int Modification,
    int Number,
    ToolDataKind Kind,
    string DisplayName)
{
    public bool IsCartographLwd4TxMode =>
        DeviceType == DeviceType.Cartograph
        && TransmitterCount == 4;
}
