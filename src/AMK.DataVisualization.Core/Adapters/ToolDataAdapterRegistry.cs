using System.Runtime.InteropServices;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Core.Adapters;

public readonly record struct ParsedToolFrame(
    ToolDataKind Kind,
    ToolCapabilities Capabilities,
    GpToolData CanonicalData);

public interface IToolDataAdapter
{
    ToolDataKind Kind { get; }

    ToolCapabilities Capabilities { get; }

    bool CanHandleSignature(uint signature);

    bool SupportsFileFormat(FileFormat format);

    /// <summary>
    /// Физический размер кадра, который отдаёт прибор с этой сигнатурой.
    /// </summary>
    int GetRawFrameSize(uint signature);

    /// <summary>
    /// Приводит кадр к canonical-структуре. Длина <paramref name="rawFrame"/>
    /// обязана совпадать с размером, который даёт сигнатура этого же кадра:
    /// расхождение означает рассинхронизацию с источником и разбором не
    /// лечится.
    /// </summary>
    bool TryParseRawFrame(ReadOnlySpan<byte> rawFrame, out ParsedToolFrame frame);
}

public sealed class UniversalCanonicalAdapter : IToolDataAdapter
{
    private const float CartographLwdAmplitudeScale = 1.0f / 4096.0f;

    // Картограф с четырьмя передатчиками использует тот же 240-байтовый
    // GP_TOOL_DATA, что и LWD. Модификация не участвует в распознавании режима.

    public ToolDataKind Kind => ToolDataKind.Unknown;

    public ToolCapabilities Capabilities => ToolCapabilities.Empty;

    public int GetRawFrameSize(uint signature)
    {
        return ToolSignature.DecodeFrameSize(signature);
    }

    public bool CanHandleSignature(uint signature)
    {
        return ToolSignature.TryDecode(signature, out _);
    }

    public bool SupportsFileFormat(FileFormat format)
    {
        return format is FileFormat.Bin or FileFormat.Biv or FileFormat.Dev;
    }

    public bool TryParseRawFrame(ReadOnlySpan<byte> rawFrame, out ParsedToolFrame frame)
    {
        frame = default;
        if (rawFrame.Length < sizeof(uint))
        {
            return false;
        }

        uint signature = MemoryMarshal.Read<uint>(rawFrame[..sizeof(uint)]);
        if (!ToolSignature.TryDecode(signature, out ToolSignatureInfo info))
        {
            return false;
        }

        // Длина кадра — не подсказка, а часть контракта: сигнатура говорит,
        // сколько байт обязан был отдать источник. Кадр другой длины означает,
        // что чтение сдвинулось, и разбирать его нельзя.
        if (rawFrame.Length != info.FrameSize)
        {
            return false;
        }

        // Кадр старой прошивки ложится в первые 240 байт canonical-структуры,
        // хвост остаётся нулевым: общая часть в новой прошивке не сдвинулась.
        GpToolData canonicalData = default;
        Span<byte> canonicalBytes = MemoryMarshal.AsBytes(new Span<GpToolData>(ref canonicalData));
        rawFrame.CopyTo(canonicalBytes);

        ToolCapabilities capabilities = BuildCapabilities(info);
        frame = new ParsedToolFrame(capabilities.Kind, capabilities, canonicalData);
        return true;
    }

    public static DeviceType DetermineDeviceType(uint toolCode)
    {
        uint hundredsDigit = toolCode / 100u;
        return hundredsDigit switch
        {
            1 => DeviceType.Autonom,
            2 => DeviceType.Lwd,
            3 => DeviceType.Cartograph,
            _ => DeviceType.Unknown
        };
    }

    public static int DetermineTransmitterCount(uint toolCode)
    {
        if (IsCartographLwd4TxMode(toolCode))
        {
            return 4;
        }

        int tensDigit = (int)((toolCode / 10u) % 10u);
        return tensDigit is >= 3 and <= 5 ? tensDigit : BinaryLayoutConstants.MaxTransmitterCount;
    }

    public static int DetermineModification(uint toolCode)
    {
        return (int)(toolCode % 10u);
    }

    public static ToolDataKind DetermineKindFromToolCode(uint toolCode)
    {
        return DetermineDeviceType(toolCode) switch
        {
            DeviceType.Autonom => ToolDataKind.Autonom,
            DeviceType.Lwd => ToolDataKind.Lwd,
            DeviceType.Cartograph when IsCartographLwd4TxMode(toolCode) => ToolDataKind.Lwd,
            _ => ToolDataKind.Unknown
        };
    }

    public static bool IsCartographLwd4TxMode(uint toolCode)
    {
        int encodedTransmitterCount = (int)((toolCode / 10u) % 10u);
        return DetermineDeviceType(toolCode) == DeviceType.Cartograph
            && encodedTransmitterCount == 4;
    }

    public static bool IsSupportedToolCode(uint toolCode)
    {
        int encodedTransmitterCount = (int)((toolCode / 10u) % 10u);
        return DetermineDeviceType(toolCode) switch
        {
            DeviceType.Autonom => encodedTransmitterCount is >= 3 and <= 5,
            DeviceType.Lwd => encodedTransmitterCount is >= 3 and <= 5,
            DeviceType.Cartograph => IsCartographLwd4TxMode(toolCode),
            _ => false
        };
    }

    public static ToolCapabilities BuildCapabilities(uint toolCode)
    {
        return BuildCapabilities(new ToolSignatureInfo(
            0,
            FirmwareGeneration.Legacy,
            BinaryLayoutConstants.LegacyFrameSize,
            toolCode,
            0));
    }

    public static ToolCapabilities BuildCapabilities(ToolSignatureInfo info)
    {
        int transmitterCount = DetermineTransmitterCount(info.ToolCode);
        ToolDataKind kind = DetermineKindFromToolCode(info.ToolCode);
        bool extended = info.Generation == FirmwareGeneration.Extended;
        return new ToolCapabilities(
            kind,
            transmitterCount,
            DetermineAmplitudeScale(info.ToolCode),
            transmitterCount >= BinaryLayoutConstants.MaxTransmitterCount,
            HasSeparatePhaseRx: true,
            HasDeltaPhase: true,
            HasZeroAmplitudeRx1: true,
            HasZeroAmplitudeRx2: true,
            HasZeroDeltaPhase: true,
            HasSondeRhoSmt: true,
            HasSondePhaseSmt: true,
            SupportsBinContainer: true,
            SupportsBivContainer: true,
            SupportsDevContainer: true,
            SupportsComPolling: true,
            SupportsSilentData: true,
            FrameSize: info.FrameSize,
            HasSondeRhoAttSmt: extended,
            HasSondeAttDbSmt: extended,
            HasAirAttenuation: extended);
    }

    public static float DetermineAmplitudeScale(uint toolCode)
    {
        // Картограф в режиме LWD передаёт активные и молчащие амплитуды
        // в Q12-масштабе. После нормализации значения совпадают с mV-контрактом
        // обычного LWD; остальные поля структуры уже используют LWD-единицы.
        return IsCartographLwd4TxMode(toolCode)
            ? CartographLwdAmplitudeScale
            : 1.0f;
    }

    public static ToolIdentity BuildIdentity(uint signature)
    {
        ToolSignatureInfo info = ToolSignature.Decode(signature);
        DeviceType deviceType = DetermineDeviceType(info.ToolCode);
        int transmitterCount = DetermineTransmitterCount(info.ToolCode);
        int modification = DetermineModification(info.ToolCode);
        ToolDataKind kind = DetermineKindFromToolCode(info.ToolCode);
        return new ToolIdentity(
            signature,
            info.ToolCode,
            deviceType,
            transmitterCount,
            modification,
            info.Number,
            kind,
            BuildDisplayName(info.ToolCode),
            info.FrameSize);
    }

    public static string BuildDisplayName(uint toolCode)
    {
        if (IsCartographLwd4TxMode(toolCode))
        {
            return "Картограф->LWD-4TX";
        }

        string typeName = DetermineDeviceType(toolCode) switch
        {
            DeviceType.Autonom => "Автоном",
            DeviceType.Lwd => "LWD",
            _ => "Неизвестно"
        };

        return $"{typeName} {DetermineTransmitterCount(toolCode)}TX";
    }
}

public sealed class ToolDataAdapterRegistry
{
    private readonly IToolDataAdapter[] adapters;

    public static ToolDataAdapterRegistry Default { get; } = new();

    public ToolDataAdapterRegistry()
    {
        adapters = [new UniversalCanonicalAdapter()];
    }

    public IToolDataAdapter? FindAdapterBySignature(uint signature)
    {
        foreach (IToolDataAdapter adapter in adapters)
        {
            if (adapter.CanHandleSignature(signature))
            {
                return adapter;
            }
        }

        return null;
    }

    public bool TryParseRawFrame(ReadOnlySpan<byte> rawFrame, out ParsedToolFrame frame)
    {
        frame = default;
        if (rawFrame.Length < sizeof(uint))
        {
            return false;
        }

        uint signature = MemoryMarshal.Read<uint>(rawFrame[..sizeof(uint)]);
        IToolDataAdapter? adapter = FindAdapterBySignature(signature);
        return adapter is not null && adapter.TryParseRawFrame(rawFrame, out frame);
    }

    public bool IsSupportedSignature(uint signature)
    {
        return FindAdapterBySignature(signature) is not null;
    }

    /// <summary>
    /// Физический размер кадра источника с такой сигнатурой.
    /// </summary>
    public static int DecodeFrameSize(uint signature)
    {
        return ToolSignature.DecodeFrameSize(signature);
    }

    public static uint DecodeToolCode(uint signature)
    {
        return ToolSignature.DecodeToolCode(signature);
    }

    /// <summary>
    /// Серийный номер прибора из младших разрядов сигнатуры — поле «Серийный №»
    /// окна метрологии и одновременно `serial_number` карточки прибора в базе
    /// учёта.
    /// </summary>
    public static int DecodeNumber(uint signature)
    {
        return ToolSignature.DecodeNumber(signature);
    }

    public static ToolDataKind DetermineToolDataKind(uint signature)
    {
        uint toolCode = DecodeToolCode(signature);
        return UniversalCanonicalAdapter.DetermineKindFromToolCode(toolCode);
    }
}

