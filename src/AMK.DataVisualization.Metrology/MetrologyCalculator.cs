using AMK.DataVisualization.Core.Access;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Metrology;

public static class MetrologyCalculator
{
    public const float AirPhaseDivisor = 57297.0f;
    private const float GoldenSection = 0.382f;
    private const float MinimumRho = 0.01f;
    private const float MaximumRho = 7000.0f;
    private const float InfiniteRho = 7200.0f;
    private const float ArgumentEpsilon = 5e-7f;
    private const float InfiniteEpsilon = 5e-9f;
    private const int MaximumIterations = 256;
    private static readonly float[] AlternatingSigns = [1.0f, -1.0f, 1.0f, -1.0f, 1.0f];

    public static bool CanCalculate(MetrologyData metrology, MeasurementView measurement)
    {
        return measurement.Signature == metrology.Signature;
    }

    public static bool TryCalculatePhaseSmt(
        MetrologyData metrology,
        MeasurementView measurement,
        int frequencyIndex,
        int transmitterIndex,
        out float phaseRadians)
    {
        phaseRadians = 0.0f;
        if (!CanCalculate(metrology, measurement)
            || frequencyIndex is < 0 or >= BinaryLayoutConstants.FrequencyCount)
        {
            return false;
        }

        int transmitterCount = Math.Clamp(
            measurement.Capabilities.TransmitterCount > 0
                ? measurement.Capabilities.TransmitterCount
                : BinaryLayoutConstants.MaxTransmitterCount,
            1,
            BinaryLayoutConstants.MaxTransmitterCount);
        if (transmitterIndex < 0 || transmitterIndex >= transmitterCount)
        {
            return false;
        }

        ToolDataAccessView view = measurement.CreateAccessView();
        Span<float> correctedDelta = stackalloc float[BinaryLayoutConstants.MaxTransmitterCount];
        for (int index = 0; index < transmitterCount; index++)
        {
            float rawDelta = view.TryGetDeltaPhase(frequencyIndex, index, out float value) ? value : 0.0f;
            float airOffset = metrology.GetAirZz(frequencyIndex, index) / AirPhaseDivisor;
            correctedDelta[index] = AlternatingSigns[index] * (rawDelta - airOffset);
        }

        byte condition = ExtractConditionByte(measurement.Record.Condition, frequencyIndex);
        float[,] matrix = GetSymmetrizationMatrix(transmitterCount, condition);
        float result = 0.0f;
        for (int input = 0; input < transmitterCount; input++)
        {
            result += matrix[transmitterIndex, input] * correctedDelta[input];
        }

        phaseRadians = result;
        return float.IsFinite(result);
    }

    public static bool TryCalculateRhoSmt(
        MetrologyData metrology,
        MeasurementView measurement,
        int frequencyIndex,
        int transmitterIndex,
        out float rho)
    {
        rho = 0.0f;
        if (!TryCalculatePhaseSmt(metrology, measurement, frequencyIndex, transmitterIndex, out float phaseRadians))
        {
            return false;
        }

        rho = InvertPhaseToRho(metrology, frequencyIndex, transmitterIndex, phaseRadians);
        return float.IsFinite(rho);
    }

    public static byte ExtractConditionByte(uint rawCondition, int frequencyIndex)
    {
        GpToolData.ValidateFrequencyIndex(frequencyIndex);
        return frequencyIndex == 0
            ? (byte)((rawCondition >> 8) & 0xFFu)
            : (byte)(rawCondition & 0xFFu);
    }

    public static float SignalPhase(MetrologyData metrology, int frequencyIndex, int transmitterIndex, float rho)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        float safeRho = rho > 0.0f ? rho : MinimumRho;
        float l1 = metrology.GetL1(transmitterIndex) / 1000.0f;
        float l2 = metrology.GetL2(transmitterIndex) / 1000.0f;
        float omega = 0.0078957f * metrology.GetFrequency(frequencyIndex) / safeRho;

        // Equivalent to std::complex<float>: ik = i * sqrt(i * omega).
        float root = MathF.Sqrt(omega * 0.5f);
        float ikReal = -root;
        float ikImaginary = root;
        float distance = l2 - l1;
        float exponentialMagnitude = MathF.Exp(ikReal * distance);
        float exponentialReal = exponentialMagnitude * MathF.Cos(ikImaginary * distance);
        float exponentialImaginary = exponentialMagnitude * MathF.Sin(ikImaginary * distance);

        float numeratorReal = 1.0f - ikReal * l2;
        float numeratorImaginary = -ikImaginary * l2;
        float denominatorReal = 1.0f - ikReal * l1;
        float denominatorImaginary = -ikImaginary * l1;
        float denominatorNorm = denominatorReal * denominatorReal + denominatorImaginary * denominatorImaginary;
        float ratioReal = (numeratorReal * denominatorReal + numeratorImaginary * denominatorImaginary) / denominatorNorm;
        float ratioImaginary = (numeratorImaginary * denominatorReal - numeratorReal * denominatorImaginary) / denominatorNorm;

        float signalReal = exponentialReal * ratioReal - exponentialImaginary * ratioImaginary;
        float signalImaginary = exponentialReal * ratioImaginary + exponentialImaginary * ratioReal;
        return MathF.Atan2(signalImaginary, signalReal);
    }

    public static float InvertPhaseToRho(
        MetrologyData metrology,
        int frequencyIndex,
        int transmitterIndex,
        float targetPhase)
    {
        float left = MinimumRho;
        float right = MaximumRho;
        for (int iteration = 0; iteration < MaximumIterations; iteration++)
        {
            float x1 = left + GoldenSection * (right - left);
            float x2 = right - GoldenSection * (right - left);
            float a = targetPhase - SignalPhase(metrology, frequencyIndex, transmitterIndex, x1);
            float b = targetPhase - SignalPhase(metrology, frequencyIndex, transmitterIndex, x2);
            if (MathF.Abs(a) > MathF.Abs(b))
            {
                left = x1;
            }
            else
            {
                right = x2;
            }

            if (MathF.Abs(a) < ArgumentEpsilon || MathF.Abs(b) < ArgumentEpsilon)
            {
                return (left + right) * 0.5f;
            }

            if (MathF.Abs(a - b) < InfiniteEpsilon)
            {
                return InfiniteRho;
            }
        }

        return (left + right) * 0.5f;
    }

    private static float[,] GetSymmetrizationMatrix(int transmitterCount, byte condition)
    {
        return transmitterCount switch
        {
            5 => GetFiveTransmitterMatrix(condition),
            4 => GetFourTransmitterMatrix(condition),
            3 => GetThreeTransmitterMatrix(condition),
            _ => GetFallbackMatrix(transmitterCount, condition)
        };
    }

    private static float[,] GetFiveTransmitterMatrix(byte condition)
    {
        return condition switch
        {
            0b00011111 or 0b11111111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [0, .25f, .5f, .25f, 0],
                [0, 0, .25f, .5f, .25f],
                [0, 0, -.25f, .5f, .75f]),
            0b00011110 => Matrix(
                [0, 1.25f, .5f, -.75f, 0],
                [0, .75f, .5f, -.25f, 0],
                [0, .25f, .5f, .25f, 0],
                [0, 0, .25f, .5f, .25f],
                [0, 0, -.25f, .5f, .75f]),
            0b00011101 => Matrix(
                [1.25f, 0, -.75f, .5f, 0],
                [.75f, 0, -.25f, .5f, 0],
                [0, 0, .75f, .5f, -.25f],
                [0, 0, .25f, .5f, .25f],
                [0, 0, -.25f, .5f, .75f]),
            0b00011011 => Matrix(
                [.5f, .75f, 0, -.25f, 0],
                [0, 1.25f, 0, -.75f, .5f],
                [0, .75f, 0, -.25f, .5f],
                [0, .25f, 0, .25f, .05f],
                [0, -.25f, 0, .75f, .05f]),
            0b00010111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [-.25f, .5f, .75f, 0, 0],
                [0, .5f, -.25f, 0, .75f],
                [0, .5f, -.75f, 0, 1.25f]),
            0b00001111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [.05f, .25f, .5f, .25f, 0],
                [0, -.25f, .5f, .75f, 0],
                [0, -.75f, .5f, 1.25f, 0]),
            0 => IdentityMatrix(5),
            _ => GetFallbackMatrix(5, condition)
        };
    }

    private static float[,] GetFourTransmitterMatrix(byte condition)
    {
        return condition switch
        {
            0b00001111 or 0b11111111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [0, .25f, .5f, .25f, 0],
                [0, -.25f, .5f, .75f, 0],
                [0, 0, 0, 0, 0]),
            0b00001110 => Matrix(
                [0, 1.25f, .5f, -.75f, 0],
                [0, .75f, .5f, -.25f, 0],
                [0, .25f, .5f, .25f, 0],
                [0, -.25f, .5f, .75f, 0],
                [0, 0, 0, 0, 0]),
            0b00001101 => Matrix(
                [1.25f, 0, -.75f, .5f, 0],
                [.75f, 0, -.25f, .5f, 0],
                [.25f, 0, .25f, .5f, 0],
                [-.25f, 0, .75f, .5f, 0],
                [0, 0, 0, 0, 0]),
            0b00001011 => Matrix(
                [.5f, .75f, 0, -.25f, 0],
                [.5f, .25f, 0, .25f, 0],
                [.5f, -.25f, 0, .75f, 0],
                [-.5f, -.75f, 0, 1.25f, 0],
                [0, 0, 0, 0, 0]),
            0b00000111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [-.25f, .5f, .75f, 0, 0],
                [-.75f, .5f, 1.25f, 0, 0],
                [0, 0, 0, 0, 0]),
            0 => IdentityMatrix(4),
            _ => GetFallbackMatrix(4, condition)
        };
    }

    private static float[,] GetThreeTransmitterMatrix(byte condition)
    {
        return condition switch
        {
            0b00000111 => Matrix(
                [.75f, .5f, -.25f, 0, 0],
                [.25f, .5f, .25f, 0, 0],
                [-.25f, .5f, .75f, 0, 0],
                [-.75f, .5f, 1.25f, 0, 0],
                [0, 0, 0, 0, 0]),
            0 => IdentityMatrix(3),
            _ => GetFallbackMatrix(3, condition)
        };
    }

    private static float[,] GetFallbackMatrix(int transmitterCount, byte condition)
    {
        float[,] matrix = new float[BinaryLayoutConstants.MaxTransmitterCount, BinaryLayoutConstants.MaxTransmitterCount];
        for (int index = 0; index < transmitterCount; index++)
        {
            if ((condition & (1 << index)) != 0)
            {
                matrix[index, index] = 1.0f;
            }
        }

        return matrix;
    }

    private static float[,] IdentityMatrix(int transmitterCount)
    {
        float[,] matrix = new float[BinaryLayoutConstants.MaxTransmitterCount, BinaryLayoutConstants.MaxTransmitterCount];
        for (int index = 0; index < transmitterCount; index++)
        {
            matrix[index, index] = 1.0f;
        }

        return matrix;
    }

    private static float[,] Matrix(params float[][] rows)
    {
        float[,] matrix = new float[BinaryLayoutConstants.MaxTransmitterCount, BinaryLayoutConstants.MaxTransmitterCount];
        for (int row = 0; row < Math.Min(rows.Length, BinaryLayoutConstants.MaxTransmitterCount); row++)
        {
            for (int column = 0; column < Math.Min(rows[row].Length, BinaryLayoutConstants.MaxTransmitterCount); column++)
            {
                matrix[row, column] = rows[row][column];
            }
        }

        return matrix;
    }
}
