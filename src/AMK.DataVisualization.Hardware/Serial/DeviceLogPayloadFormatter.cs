using System.Globalization;
using System.Text;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Hardware.Serial;

internal static class DeviceLogPayloadFormatter
{
    public static void Append(ParsedToolFrame parsedFrame)
    {
        GpToolData data = parsedFrame.CanonicalData;
        int transmitterCount = Math.Clamp(
            parsedFrame.Capabilities.TransmitterCount > 0
                ? parsedFrame.Capabilities.TransmitterCount
                : BinaryLayoutConstants.MaxTransmitterCount,
            1,
            BinaryLayoutConstants.MaxTransmitterCount);

        DeviceExchangeLog.AppendRaw(string.Empty);
        DeviceExchangeLog.AppendRaw(string.Empty);
        DeviceExchangeLog.AppendRaw("───────────────────────────────────────────────────────────────────");
        DeviceExchangeLog.AppendRawBold(
            $"▶ Кадр #{data.Frame} | signature=0x{data.Signature:X8} | condition={data.Condition} | " +
            $"T={Format(data.Temperature)} °C");
        DeviceExchangeLog.AppendRaw(FormatFields(data, transmitterCount));
    }

    private static string FormatFields(GpToolData data, int transmitterCount)
    {
        StringBuilder builder = new();
        AppendLine(builder, "[GP_TOOL_DATA — разбор полей]");
        AppendLine(builder, string.Empty);
        AppendLine(builder, "УЭС (rho_smt), Ом·м");
        AppendChannels(builder, transmitterCount, data.GetRhoSmt);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Симметризованные фазы (phase_smt)");
        AppendChannels(builder, transmitterCount, data.GetPhaseSmt);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Амплитуды приёмника 1 (AM_RX_1)");
        AppendChannels(builder, transmitterCount, data.GetAmplitudeRx1);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Амплитуды приёмника 2 (AM_RX_2)");
        AppendChannels(builder, transmitterCount, data.GetAmplitudeRx2);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Разность фаз (DELTA_PH)");
        AppendChannels(builder, transmitterCount, data.GetDeltaPhase);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Молчание (молчащие передатчики)");
        AppendLine(
            builder,
            $"  ZERO_AM_RX_1:  400 кГц={Format(data.GetZeroAmplitudeRx1(0))}  " +
            $"2000 кГц={Format(data.GetZeroAmplitudeRx1(1))}");
        AppendLine(
            builder,
            $"  ZERO_AM_RX_2:  400 кГц={Format(data.GetZeroAmplitudeRx2(0))}  " +
            $"2000 кГц={Format(data.GetZeroAmplitudeRx2(1))}");
        AppendLine(
            builder,
            $"  ZERO_dPH:      400 кГц={Format(data.GetZeroDeltaPhase(0))}  " +
            $"2000 кГц={Format(data.GetZeroDeltaPhase(1))}");
        return builder.ToString();
    }

    private static void AppendChannels(
        StringBuilder builder,
        int transmitterCount,
        Func<int, int, float> valueProvider)
    {
        for (int frequencyIndex = 0; frequencyIndex < BinaryLayoutConstants.FrequencyCount; frequencyIndex++)
        {
            string frequencyLabel = frequencyIndex == 0 ? "400 кГц" : "2000 кГц";
            StringBuilder line = new($"  {frequencyLabel}:");
            for (int transmitterIndex = 0; transmitterIndex < transmitterCount; transmitterIndex++)
            {
                line.Append($"  T{transmitterIndex + 1}={Format(valueProvider(frequencyIndex, transmitterIndex))}");
            }

            AppendLine(builder, line.ToString());
        }
    }

    private static void AppendLine(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
        {
            builder.Append("\r\n");
        }

        builder.Append(line);
    }

    private static string Format(float value)
    {
        return value.ToString("G6", CultureInfo.InvariantCulture);
    }
}
