using System.Runtime.InteropServices;

namespace AMK.DataVisualization.Core.Binary;

public static class BinaryLayoutConstants
{
    public const int FrequencyCount = 2;
    public const int MaxTransmitterCount = 5;
    public const int ChannelCount = FrequencyCount * MaxTransmitterCount;
    public const int GpToolDataSize = 240;
    public const int MetrologyDataSize = 240;
    public const int DevHeaderSize = 11;
    public const int DevPhysicalFrameSize = DevHeaderSize + GpToolDataSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct GpToolData
{
    public uint Signature;
    public uint Condition;
    public uint Frame;
    public float Temperature;
    private fixed float rhoSmt[BinaryLayoutConstants.ChannelCount];
    private fixed float phaseSmt[BinaryLayoutConstants.ChannelCount];
    private fixed float amplitudeRx1[BinaryLayoutConstants.ChannelCount];
    private fixed float zeroAmplitudeRx1[BinaryLayoutConstants.FrequencyCount];
    private fixed float amplitudeRx2[BinaryLayoutConstants.ChannelCount];
    private fixed float zeroAmplitudeRx2[BinaryLayoutConstants.FrequencyCount];
    private fixed float deltaPhase[BinaryLayoutConstants.ChannelCount];
    private fixed float zeroDeltaPhase[BinaryLayoutConstants.FrequencyCount];

    public readonly float GetRhoSmt(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = rhoSmt)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetRhoSmt(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = rhoSmt)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetPhaseSmt(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = phaseSmt)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetPhaseSmt(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = phaseSmt)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetAmplitudeRx1(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = amplitudeRx1)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAmplitudeRx1(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = amplitudeRx1)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetAmplitudeRx2(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = amplitudeRx2)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAmplitudeRx2(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = amplitudeRx2)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetDeltaPhase(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = deltaPhase)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetDeltaPhase(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = deltaPhase)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetZeroAmplitudeRx1(int frequencyIndex)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroAmplitudeRx1)
        {
            return values[frequencyIndex];
        }
    }

    public void SetZeroAmplitudeRx1(int frequencyIndex, float value)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroAmplitudeRx1)
        {
            values[frequencyIndex] = value;
        }
    }

    public readonly float GetZeroAmplitudeRx2(int frequencyIndex)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroAmplitudeRx2)
        {
            return values[frequencyIndex];
        }
    }

    public void SetZeroAmplitudeRx2(int frequencyIndex, float value)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroAmplitudeRx2)
        {
            values[frequencyIndex] = value;
        }
    }

    public readonly float GetZeroDeltaPhase(int frequencyIndex)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroDeltaPhase)
        {
            return values[frequencyIndex];
        }
    }

    public void SetZeroDeltaPhase(int frequencyIndex, float value)
    {
        ValidateFrequencyIndex(frequencyIndex);
        fixed (float* values = zeroDeltaPhase)
        {
            values[frequencyIndex] = value;
        }
    }

    public static int ChannelIndex(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        return frequencyIndex * BinaryLayoutConstants.MaxTransmitterCount + transmitterIndex;
    }

    public static void ValidateChannelIndex(int frequencyIndex, int transmitterIndex)
    {
        ValidateFrequencyIndex(frequencyIndex);
        if (transmitterIndex < 0 || transmitterIndex >= BinaryLayoutConstants.MaxTransmitterCount)
        {
            throw new ArgumentOutOfRangeException(nameof(transmitterIndex));
        }
    }

    public static void ValidateFrequencyIndex(int frequencyIndex)
    {
        if (frequencyIndex < 0 || frequencyIndex >= BinaryLayoutConstants.FrequencyCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyIndex));
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MetrologyData
{
    public uint Signature;
    public uint Serial;
    private fixed ushort l1[BinaryLayoutConstants.MaxTransmitterCount];
    private fixed ushort l2[BinaryLayoutConstants.MaxTransmitterCount];
    private fixed ushort frequencies[BinaryLayoutConstants.FrequencyCount];
    private fixed short airZz[BinaryLayoutConstants.ChannelCount];
    private fixed short airZzAmplitude[BinaryLayoutConstants.ChannelCount];
    public uint SondeDiameterMm;
    private fixed byte service[164];

    public readonly ushort GetL1(int transmitterIndex)
    {
        ValidateTransmitterIndex(transmitterIndex);
        fixed (ushort* values = l1)
        {
            return values[transmitterIndex];
        }
    }

    public void SetL1(int transmitterIndex, ushort value)
    {
        ValidateTransmitterIndex(transmitterIndex);
        fixed (ushort* values = l1)
        {
            values[transmitterIndex] = value;
        }
    }

    public readonly ushort GetL2(int transmitterIndex)
    {
        ValidateTransmitterIndex(transmitterIndex);
        fixed (ushort* values = l2)
        {
            return values[transmitterIndex];
        }
    }

    public void SetL2(int transmitterIndex, ushort value)
    {
        ValidateTransmitterIndex(transmitterIndex);
        fixed (ushort* values = l2)
        {
            values[transmitterIndex] = value;
        }
    }

    public readonly ushort GetFrequency(int frequencyIndex)
    {
        GpToolData.ValidateFrequencyIndex(frequencyIndex);
        fixed (ushort* values = frequencies)
        {
            return values[frequencyIndex];
        }
    }

    public void SetFrequency(int frequencyIndex, ushort value)
    {
        GpToolData.ValidateFrequencyIndex(frequencyIndex);
        fixed (ushort* values = frequencies)
        {
            values[frequencyIndex] = value;
        }
    }

    public readonly short GetAirZz(int frequencyIndex, int transmitterIndex)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (short* values = airZz)
        {
            return values[GpToolData.ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAirZz(int frequencyIndex, int transmitterIndex, short value)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (short* values = airZz)
        {
            values[GpToolData.ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly short GetAirZzAmplitude(int frequencyIndex, int transmitterIndex)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (short* values = airZzAmplitude)
        {
            return values[GpToolData.ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAirZzAmplitude(int frequencyIndex, int transmitterIndex, short value)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (short* values = airZzAmplitude)
        {
            values[GpToolData.ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    private static void ValidateTransmitterIndex(int transmitterIndex)
    {
        if (transmitterIndex < 0 || transmitterIndex >= BinaryLayoutConstants.MaxTransmitterCount)
        {
            throw new ArgumentOutOfRangeException(nameof(transmitterIndex));
        }
    }
}

public static class BinaryLayoutVerifier
{
    public static unsafe int GpToolDataSize => sizeof(GpToolData);

    public static unsafe int MetrologyDataSize => sizeof(MetrologyData);

    public static void ThrowIfInvalid()
    {
        if (GpToolDataSize != BinaryLayoutConstants.GpToolDataSize)
        {
            throw new InvalidOperationException($"GpToolData size must be {BinaryLayoutConstants.GpToolDataSize} bytes, actual {GpToolDataSize}.");
        }

        if (MetrologyDataSize != BinaryLayoutConstants.MetrologyDataSize)
        {
            throw new InvalidOperationException($"MetrologyData size must be {BinaryLayoutConstants.MetrologyDataSize} bytes, actual {MetrologyDataSize}.");
        }
    }
}
