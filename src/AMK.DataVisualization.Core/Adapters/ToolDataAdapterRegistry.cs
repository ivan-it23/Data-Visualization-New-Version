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

    int RawFrameSize { get; }

    bool TryParseRawFrame(ReadOnlySpan<byte> rawFrame, out ParsedToolFrame frame);
}

public sealed class UniversalCanonicalAdapter : IToolDataAdapter
{
    private const float CartographLwdAmplitudeScale = 1.0f / 4096.0f;

    // Картограф с четырьмя передатчиками использует тот же 240-байтовый
    // GP_TOOL_DATA, что и LWD. Модификация не участвует в распознавании режима.

    public ToolDataKind Kind => ToolDataKind.Unknown;

    public ToolCapabilities Capabilities => ToolCapabilities.Empty;

    public int RawFrameSize => BinaryLayoutConstants.GpToolDataSize;

    public bool CanHandleSignature(uint signature)
    {
        return IsSupportedToolCode(ToolDataAdapterRegistry.DecodeToolCode(signature));
    }

    public bool SupportsFileFormat(FileFormat format)
    {
        return format is FileFormat.Bin or FileFormat.Biv or FileFormat.Dev;
    }

    public bool TryParseRawFrame(ReadOnlySpan<byte> rawFrame, out ParsedToolFrame frame)
    {
        frame = default;
        if (rawFrame.Length != BinaryLayoutConstants.GpToolDataSize)
        {
            return false;
        }

        GpToolData canonicalData = MemoryMarshal.Read<GpToolData>(rawFrame);
        if (!CanHandleSignature(canonicalData.Signature))
        {
            return false;
        }

        uint toolCode = ToolDataAdapterRegistry.DecodeToolCode(canonicalData.Signature);
        ToolDataKind kind = DetermineKindFromToolCode(toolCode);
        ToolCapabilities capabilities = BuildCapabilities(toolCode);
        frame = new ParsedToolFrame(kind, capabilities, canonicalData);
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
        int transmitterCount = DetermineTransmitterCount(toolCode);
        ToolDataKind kind = DetermineKindFromToolCode(toolCode);
        return new ToolCapabilities(
            kind,
            transmitterCount,
            DetermineAmplitudeScale(toolCode),
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
            SupportsSilentData: true);
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
        uint toolCode = ToolDataAdapterRegistry.DecodeToolCode(signature);
        DeviceType deviceType = DetermineDeviceType(toolCode);
        int transmitterCount = DetermineTransmitterCount(toolCode);
        int modification = DetermineModification(toolCode);
        ToolDataKind kind = DetermineKindFromToolCode(toolCode);
        return new ToolIdentity(signature, toolCode, deviceType, transmitterCount, modification, kind, BuildDisplayName(toolCode));
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

    public static uint DecodeToolCode(uint signature)
    {
        uint low20 = (signature << 12) >> 12;
        return low20 / 1000u;
    }

    public static ToolDataKind DetermineToolDataKind(uint signature)
    {
        uint toolCode = DecodeToolCode(signature);
        return UniversalCanonicalAdapter.DetermineKindFromToolCode(toolCode);
    }
}

