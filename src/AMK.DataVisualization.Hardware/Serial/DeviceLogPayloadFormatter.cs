using System.Globalization;
using System.Text;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Hardware.Serial;

/// <summary>
/// Разбор принятого кадра для режима «Сырые данные» окна логов.
/// </summary>
/// <remarks>
/// Выводятся все поля структуры измерений без исключений: служебные, все
/// массивы по каналам и молчащие каналы. Значения показываются ровно такими,
/// какими пришли от прибора, — приведение амплитуд к общим единицам выполняет
/// слой доступа к данным, и в журнал оно не попадает, иначе по журналу нельзя
/// было бы сверить байты с ответом прибора.
/// </remarks>
internal static class DeviceLogPayloadFormatter
{
    public static void Append(
        ParsedToolFrame parsedFrame,
        long sessionFrameNumber,
        ReadOnlySpan<byte> rawPayload)
    {
        GpToolData data = parsedFrame.CanonicalData;
        ToolCapabilities capabilities = parsedFrame.Capabilities;
        int transmitterCount = Math.Clamp(
            capabilities.TransmitterCount > 0
                ? capabilities.TransmitterCount
                : BinaryLayoutConstants.MaxTransmitterCount,
            1,
            BinaryLayoutConstants.MaxTransmitterCount);

        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(data.Signature);

        DeviceExchangeLog.AppendRaw(string.Empty);
        DeviceExchangeLog.AppendRaw(string.Empty);
        DeviceExchangeLog.AppendRaw("───────────────────────────────────────────────────────────────────");
        DeviceExchangeLog.AppendRawBold(
            $"▶ Кадр #{sessionFrameNumber} | signature=0x{data.Signature:X8} | {identity.DisplayName} | " +
            $"прошивка: {(identity.IsExtendedFirmware ? "новая" : "старая")}, кадр {identity.FrameSize} Б | " +
            $"принято {rawPayload.Length} Б");
        DeviceExchangeLog.AppendRaw(DeviceProtocolLog.FormatRawPacket("RX сырые", rawPayload));
        DeviceExchangeLog.AppendRaw(FormatFields(data, capabilities, transmitterCount));
    }

    private static string FormatFields(GpToolData data, ToolCapabilities capabilities, int transmitterCount)
    {
        StringBuilder builder = new();
        AppendLine(builder, "[GP_TOOL_DATA — разбор полей]");

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Служебные поля");
        AppendLine(builder, $"  signature   = 0x{data.Signature:X8} ({data.Signature})");
        AppendLine(builder, $"  condition   = 0x{data.Condition:X8}  {FormatConditionBits(data.Condition)}");
        AppendLine(builder, $"  {FormatConditionChannels(data.Condition, transmitterCount)}");
        AppendLine(builder, $"  frame       = {data.Frame}");
        AppendLine(builder, $"  temperature = {Format(data.Temperature)} °C");

        AppendLine(builder, string.Empty);
        AppendLine(builder, "УЭС (rho_smt), Ом·м");
        AppendChannels(builder, transmitterCount, data.GetRhoSmt);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Симметризованные фазы (phase_smt), рад");
        AppendChannels(builder, transmitterCount, data.GetPhaseSmt);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Амплитуды приёмника 1 (AM_RX_1)");
        AppendChannels(builder, transmitterCount, data.GetAmplitudeRx1);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Амплитуды приёмника 2 (AM_RX_2)");
        AppendChannels(builder, transmitterCount, data.GetAmplitudeRx2);

        AppendLine(builder, string.Empty);
        AppendLine(builder, "Разность фаз (DELTA_PH), рад");
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

        // Поля, которые есть только у новой прошивки. У кадра старого прибора
        // это место занимает не прибор, а нули, дописанные при приведении к
        // общей структуре, поэтому вместо чисел выводится причина их отсутствия.
        AppendLine(builder, string.Empty);
        if (capabilities.HasSondeRhoAttSmt)
        {
            AppendLine(builder, "Амплитудные УЭС (rho_att_smt), Ом·м");
            AppendChannels(builder, transmitterCount, data.GetRhoAttSmt);
        }
        else
        {
            AppendLine(builder, "Амплитудные УЭС (rho_att_smt): прибор старой прошивки их не передаёт");
        }

        AppendLine(builder, string.Empty);
        if (capabilities.HasSondeAttDbSmt)
        {
            AppendLine(builder, "Симметризованные затухания (att_dB_smt), дБ");
            AppendChannels(builder, transmitterCount, data.GetAttDbSmt);
        }
        else
        {
            AppendLine(builder, "Симметризованные затухания (att_dB_smt): прибор старой прошивки их не передаёт");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Байт состояния в двоичном виде, разбитый по частотам.
    /// </summary>
    private static string FormatConditionBits(uint condition)
    {
        string bits = Convert.ToString(condition, 2).PadLeft(32, '0');
        return $"[{bits[..8]} {bits[8..16]} {bits[16..24]} {bits[24..]}]";
    }

    /// <summary>
    /// Расшифровка байта состояния по передатчикам. Раскладку задаёт
    /// <see cref="GpToolData.GetConditionByte"/> — здесь только вывод.
    /// </summary>
    private static string FormatConditionChannels(uint condition, int transmitterCount)
    {
        StringBuilder builder = new("рабочие каналы:");
        for (int frequencyIndex = 0; frequencyIndex < BinaryLayoutConstants.FrequencyCount; frequencyIndex++)
        {
            byte conditionByte = GpToolData.GetConditionByte(condition, frequencyIndex);
            builder.Append(frequencyIndex == 0 ? "  400 кГц " : "  |  2000 кГц ");
            for (int transmitterIndex = 0; transmitterIndex < transmitterCount; transmitterIndex++)
            {
                bool working = (conditionByte & (1 << transmitterIndex)) != 0;
                builder.Append($"T{transmitterIndex + 1}={(working ? '1' : '0')} ");
            }
        }

        return builder.ToString().TrimEnd();
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
