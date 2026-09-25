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

/// <param name="FrameSize">
/// Физический размер кадра измерений источника: 240 байт у старой прошивки,
/// 320 у новой. В памяти кадр всегда приведён к 320 байтам, но на диск и в
/// порт уходит ровно столько байт, сколько отдал прибор.
/// </param>
/// <param name="HasSondeRhoAttSmt">
/// Прибор считает амплитудные УЭС сам. Только новая прошивка.
/// </param>
/// <param name="HasSondeAttDbSmt">
/// Прибор считает симметризованные затухания сам. Только новая прошивка.
/// </param>
/// <param name="HasAirAttenuation">
/// В метрологии прибора есть воздушные поправки затухания. Только новая
/// прошивка; у старой на этом месте нерасшифрованная служебная область.
/// </param>
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
    bool SupportsSilentData,
    int FrameSize,
    bool HasSondeRhoAttSmt,
    bool HasSondeAttDbSmt,
    bool HasAirAttenuation)
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
        false,
        Binary.BinaryLayoutConstants.LegacyFrameSize,
        false,
        false,
        false);

    /// <summary>
    /// Поколение прошивки источника. Единственный признак — наличие
    /// расширенных полей, а они выводятся из сигнатуры.
    /// </summary>
    public bool IsExtendedFirmware => FrameSize == Binary.BinaryLayoutConstants.ExtendedFrameSize;
}

/// <param name="Preamble">
/// Служебный заголовок записи `.DEV`. У записей `.bin` и `.biv` и у кадров,
/// принятых от прибора, его нет: заголовок пишет не прошивка обмена, а тот
/// модуль, что складывает дамп в память прибора.
///
/// Возможности прибора сюда не входят намеренно. <see cref="ToolCapabilities"/>
/// выводятся из сигнатуры и описывают прибор, а заголовок — свойство
/// контейнера: один и тот же прибор пишет `.bin` без него и `.DEV` с ним.
/// </param>
public sealed record ToolRecordMetadata(
    ToolDataKind Kind,
    ToolCapabilities Capabilities,
    Binary.DevPreamble? Preamble = null)
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
    string DisplayName,
    int FrameSize)
{
    /// <summary>
    /// Прибор работает под новой прошивкой: кадр измерений 320 байт, в
    /// метрологии есть воздушные поправки затухания.
    /// </summary>
    public bool IsExtendedFirmware => FrameSize == Binary.BinaryLayoutConstants.ExtendedFrameSize;


    public bool IsCartographLwd4TxMode =>
        DeviceType == DeviceType.Cartograph
        && TransmitterCount == 4;

    /// <summary>
    /// Прибор отвечает пакетом без поля длины. Единственный такой случай —
    /// Картограф в режиме LWD 4tx старой прошивки.
    /// </summary>
    /// <remarks>
    /// Картограф с новой прошивкой отвечает как все приборы этого поколения:
    /// <c>LWD_1_DDS_ATT_UES.c</c> не различает типы приборов и собирает ответ
    /// на <c>0x11</c> как <c>[CMD][struct_size + 5 : 2][кадр][CRC]</c>
    /// (<c>WorkCyclogramStateCommandHandler</c>, ветка <c>GetDataCommand</c>).
    /// Подтверждено прибором 320349001: ответ <c>11 45 01 …</c>, где
    /// <c>45 01</c> — поле длины 325.
    /// </remarks>
    public bool UsesFixedLengthTransport =>
        IsCartographLwd4TxMode
        && !IsExtendedFirmware;
}
