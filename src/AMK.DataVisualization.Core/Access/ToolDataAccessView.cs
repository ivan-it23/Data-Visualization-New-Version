using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Core.Access;

public readonly struct ToolDataAccessView
{
    private readonly GpToolData record;

    public ToolDataAccessView(GpToolData record, ToolCapabilities capabilities)
    {
        this.record = record;
        Capabilities = capabilities;
        IsValid = true;
    }

    public bool IsValid { get; }

    public ToolCapabilities Capabilities { get; }

    public ToolDataKind Kind => Capabilities.Kind;

    public int TransmitterCount => Capabilities.TransmitterCount > 0
        ? Capabilities.TransmitterCount
        : BinaryLayoutConstants.MaxTransmitterCount;

    public bool SupportsTransmitter(int transmitterIndex)
    {
        return transmitterIndex >= 0 && transmitterIndex < TransmitterCount;
    }

    public bool TryGetDeltaPhase(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex) || !Capabilities.HasDeltaPhase)
        {
            return false;
        }

        value = record.GetDeltaPhase(frequencyIndex, transmitterIndex);
        return true;
    }

    public bool TryGetAmplitudeRx1(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex))
        {
            return false;
        }

        value = ScaleAmplitude(record.GetAmplitudeRx1(frequencyIndex, transmitterIndex));
        return true;
    }

    public bool TryGetAmplitudeRx2(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex))
        {
            return false;
        }

        value = ScaleAmplitude(record.GetAmplitudeRx2(frequencyIndex, transmitterIndex));
        return true;
    }

    public bool TryGetPhaseSmt(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex) || !Capabilities.HasSondePhaseSmt)
        {
            return false;
        }

        value = record.GetPhaseSmt(frequencyIndex, transmitterIndex);
        return true;
    }

    public bool TryGetRhoSmt(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex) || !Capabilities.HasSondeRhoSmt)
        {
            return false;
        }

        value = record.GetRhoSmt(frequencyIndex, transmitterIndex);
        return true;
    }

    /// <summary>
    /// Амплитудное УЭС, рассчитанное прибором. Есть только у новой прошивки:
    /// у кадра старого прибора это место занято нулями.
    /// </summary>
    public bool TryGetRhoAttSmt(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex) || !Capabilities.HasSondeRhoAttSmt)
        {
            return false;
        }

        value = record.GetRhoAttSmt(frequencyIndex, transmitterIndex);
        return true;
    }

    /// <summary>
    /// Симметризованное затухание в децибелах, рассчитанное прибором. Есть
    /// только у новой прошивки.
    /// </summary>
    public bool TryGetAttDbSmt(int frequencyIndex, int transmitterIndex, out float value)
    {
        value = 0;
        if (!CanReadChannel(frequencyIndex, transmitterIndex) || !Capabilities.HasSondeAttDbSmt)
        {
            return false;
        }

        value = record.GetAttDbSmt(frequencyIndex, transmitterIndex);
        return true;
    }

    public bool TryGetSilentAmplitudeRx1(int frequencyIndex, out float value)
    {
        value = 0;
        if (!CanReadFrequency(frequencyIndex) || !Capabilities.HasZeroAmplitudeRx1)
        {
            return false;
        }

        value = ScaleAmplitude(record.GetZeroAmplitudeRx1(frequencyIndex));
        return true;
    }

    public bool TryGetSilentAmplitudeRx2(int frequencyIndex, out float value)
    {
        value = 0;
        if (!CanReadFrequency(frequencyIndex) || !Capabilities.HasZeroAmplitudeRx2)
        {
            return false;
        }

        value = ScaleAmplitude(record.GetZeroAmplitudeRx2(frequencyIndex));
        return true;
    }

    public bool TryGetSilentDeltaPhase(int frequencyIndex, out float value)
    {
        value = 0;
        if (!CanReadFrequency(frequencyIndex) || !Capabilities.HasZeroDeltaPhase)
        {
            return false;
        }

        value = record.GetZeroDeltaPhase(frequencyIndex);
        return true;
    }

    public float Temperature => IsValid ? record.Temperature : 0;

    private float ScaleAmplitude(float value)
    {
        return value * Capabilities.AmplitudeScale;
    }

    private bool CanReadChannel(int frequencyIndex, int transmitterIndex)
    {
        return IsValid
            && frequencyIndex >= 0
            && frequencyIndex < BinaryLayoutConstants.FrequencyCount
            && SupportsTransmitter(transmitterIndex);
    }

    private bool CanReadFrequency(int frequencyIndex)
    {
        return IsValid
            && frequencyIndex >= 0
            && frequencyIndex < BinaryLayoutConstants.FrequencyCount;
    }
}

public sealed class MeasurementView
{
    private readonly GpToolData record;

    public MeasurementView(GpToolData record, ToolRecordMetadata metadata)
    {
        this.record = record;
        Metadata = metadata;
    }

    public static MeasurementView FromParsedFrame(ParsedToolFrame frame)
    {
        return new MeasurementView(frame.CanonicalData, new ToolRecordMetadata(frame.Kind, frame.Capabilities));
    }

    public ToolRecordMetadata Metadata { get; }

    public ToolDataKind Kind => Metadata.Kind;

    public ToolCapabilities Capabilities => Metadata.Capabilities;

    /// <summary>
    /// Служебный заголовок записи `.DEV`, если источник его имеет.
    /// </summary>
    public DevPreamble? Preamble => Metadata.Preamble;

    public uint Signature => record.Signature;

    public float Temperature => record.Temperature;

    public GpToolData Record => record;

    public ToolDataAccessView CreateAccessView()
    {
        return new ToolDataAccessView(record, Metadata.Capabilities);
    }
}
