using System.Runtime.InteropServices;

namespace AMK.DataVisualization.Core.Binary;

public static class BinaryLayoutConstants
{
    public const int FrequencyCount = 2;
    public const int MaxTransmitterCount = 5;
    public const int ChannelCount = FrequencyCount * MaxTransmitterCount;

    /// <summary>
    /// Кадр измерений старой прошивки. Формат приборов в эксплуатации;
    /// не меняется.
    /// </summary>
    public const int LegacyFrameSize = 240;

    /// <summary>
    /// Кадр измерений новой прошивки: те же 240 байт, за которыми идут
    /// амплитудные УЭС и симметризованные затухания.
    /// </summary>
    public const int ExtendedFrameSize = 320;

    /// <summary>
    /// Размер <see cref="GpToolData"/> — структуры, к которой приводятся кадры
    /// обоих поколений. Всегда расширенный: кадр старой прошивки занимает
    /// первые 240 байт, хвост остаётся нулевым.
    /// </summary>
    public const int CanonicalFrameSize = ExtendedFrameSize;

    /// <summary>
    /// Метрология одинакова для обоих поколений — ровно 240 байт. Новая
    /// прошивка перекроила служебную область, но не размер.
    /// </summary>
    public const int MetrologyDataSize = 240;

    /// <summary>
    /// Смещение, по которому кончается часть метрологии, общая для обоих
    /// поколений прошивки. Дальше у старой прошивки идёт служебная область,
    /// у новой — воздушные поправки затухания.
    /// </summary>
    public const int MetrologyCommonSize = 84;

    /// <summary>
    /// Смещение отметки времени записи метрологии. Поле появилось в новой
    /// прошивке за счёт служебной области, сокращённой с 58 до 54 слов; у
    /// старой прошивки по этим восьми байтам лежит её служебная область.
    /// </summary>
    public const int MetrologyTimestampOffset = 124;

    public const int DevHeaderSize = 11;

    /// <summary>
    /// Размер физической записи `.DEV`: служебный заголовок плюс кадр
    /// измерений источника.
    /// </summary>
    public static int DevPhysicalFrameSize(int frameSize)
    {
        return DevHeaderSize + frameSize;
    }
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

    // Здесь кончается кадр старой прошивки. Поля ниже приходят только от
    // приборов с новой прошивкой; у кадра, прочитанного из 240-байтового
    // источника, они нулевые, и доступ к ним закрыт возможностями источника.

    /// <summary>Амплитудные УЭС, рассчитанные на контроллере прибора.</summary>
    private fixed float rhoAttSmt[BinaryLayoutConstants.ChannelCount];

    /// <summary>Симметризованные затухания в децибелах, расчёт на контроллере.</summary>
    private fixed float attDbSmt[BinaryLayoutConstants.ChannelCount];

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

    public readonly float GetRhoAttSmt(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = rhoAttSmt)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetRhoAttSmt(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = rhoAttSmt)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    public readonly float GetAttDbSmt(int frequencyIndex, int transmitterIndex)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = attDbSmt)
        {
            return values[ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAttDbSmt(int frequencyIndex, int transmitterIndex, float value)
    {
        ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = attDbSmt)
        {
            values[ChannelIndex(frequencyIndex, transmitterIndex)] = value;
        }
    }

    /// <summary>
    /// Байт состояния для одной частоты: бит передатчика взведён, если прибор
    /// считает канал рабочим.
    /// </summary>
    /// <remarks>
    /// Раскладка поля <c>condition</c>: биты 8…12 — 400 кГц T1…T5, биты 0…4 —
    /// 2000 кГц. Она часть двоичного контракта, поэтому живёт здесь, рядом со
    /// структурой, а не повторяется в расчётах и в журнале обмена.
    /// </remarks>
    public static byte GetConditionByte(uint condition, int frequencyIndex)
    {
        ValidateFrequencyIndex(frequencyIndex);
        return frequencyIndex == 0
            ? (byte)((condition >> 8) & 0xFFu)
            : (byte)(condition & 0xFFu);
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
    public uint WorkType;
    public uint RxPosition;

    // Здесь кончается часть, общая для обоих поколений прошивки.
    //
    // Старая прошивка держит дальше служебную область ushort[78]. Новая
    // забирает из неё сначала 40 байт под воздушные поправки затухания, затем
    // ещё 8 под отметку времени записи, и оставляет ushort[54]; общий размер
    // структуры не изменился и остался 240 байт.
    //
    // Читать и писать airAttDb и TimestampUnixSeconds допустимо только у
    // приборов новой прошивки: для старого прибора это его служебная область,
    // и запись туда испортит содержимое, смысл которого приложению неизвестен.
    private fixed float airAttDb[BinaryLayoutConstants.ChannelCount];

    /// <summary>
    /// Время записи метрологии в прибор или в файл — секунды Unix-эпохи в UTC.
    /// Проставляется приложением по часам компьютера в момент записи; прибор
    /// это поле не меняет.
    /// </summary>
    /// <remarks>
    /// Есть только у приборов новой прошивки — вызывающий обязан проверить
    /// это по поколению источника, как и для <c>airAttDb</c>. Значение
    /// разбирается и форматируется в <c>MetrologyTimestamp</c>: у прибора с
    /// неразмеченной памятью здесь лежат байты <c>0xFF</c>, а у метрологии,
    /// записанной до появления поля, — нули, и ни то ни другое датой не
    /// является.
    /// </remarks>
    public long TimestampUnixSeconds;

    private fixed ushort service[54];

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

    /// <summary>
    /// Воздушная поправка затухания в децибелах. Присутствует только в
    /// метрологии новой прошивки; вызывающий обязан проверить это по
    /// возможностям источника.
    /// </summary>
    public readonly float GetAirAttDb(int frequencyIndex, int transmitterIndex)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = airAttDb)
        {
            return values[GpToolData.ChannelIndex(frequencyIndex, transmitterIndex)];
        }
    }

    public void SetAirAttDb(int frequencyIndex, int transmitterIndex, float value)
    {
        GpToolData.ValidateChannelIndex(frequencyIndex, transmitterIndex);
        fixed (float* values = airAttDb)
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

    /// <summary>
    /// Все байты установлены в единицы: первый ненулевой байт структуры после
    /// такой записи и есть смещение поля.
    /// </summary>
    private static readonly float FieldProbe = BitConverter.UInt32BitsToSingle(0xFFFFFFFFu);

    /// <summary>
    /// Смещение, с которого в <see cref="GpToolData"/> начинаются поля новой
    /// прошивки. Приведение 240-байтового кадра к canonical опирается на то,
    /// что общая часть не сдвинулась ни на байт, поэтому смещение проверяется
    /// на старте вместе с размерами.
    /// </summary>
    public static unsafe int ExtendedFieldsOffset
    {
        get
        {
            GpToolData probe = default;
            probe.SetRhoAttSmt(0, 0, FieldProbe);
            byte* origin = (byte*)&probe;
            for (int offset = 0; offset < sizeof(GpToolData); offset++)
            {
                if (origin[offset] != 0)
                {
                    return offset;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// Смещение воздушных поправок затухания в метрологии новой прошивки.
    /// </summary>
    public static unsafe int AirAttDbOffset
    {
        get
        {
            MetrologyData probe = default;
            probe.SetAirAttDb(0, 0, FieldProbe);
            byte* origin = (byte*)&probe;
            for (int offset = 0; offset < sizeof(MetrologyData); offset++)
            {
                if (origin[offset] != 0)
                {
                    return offset;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// Смещение отметки времени записи в метрологии новой прошивки.
    /// </summary>
    public static unsafe int TimestampOffset
    {
        get
        {
            MetrologyData probe = default;
            probe.TimestampUnixSeconds = -1;
            byte* origin = (byte*)&probe;
            for (int offset = 0; offset < sizeof(MetrologyData); offset++)
            {
                if (origin[offset] != 0)
                {
                    return offset;
                }
            }

            return -1;
        }
    }

    public static void ThrowIfInvalid()
    {
        if (GpToolDataSize != BinaryLayoutConstants.CanonicalFrameSize)
        {
            throw new InvalidOperationException($"GpToolData size must be {BinaryLayoutConstants.CanonicalFrameSize} bytes, actual {GpToolDataSize}.");
        }

        if (ExtendedFieldsOffset != BinaryLayoutConstants.LegacyFrameSize)
        {
            throw new InvalidOperationException(
                $"GpToolData extended fields must start at {BinaryLayoutConstants.LegacyFrameSize} bytes, actual {ExtendedFieldsOffset}.");
        }

        if (AirAttDbOffset != BinaryLayoutConstants.MetrologyCommonSize)
        {
            throw new InvalidOperationException(
                $"MetrologyData air attenuation must start at {BinaryLayoutConstants.MetrologyCommonSize} bytes, actual {AirAttDbOffset}.");
        }

        if (TimestampOffset != BinaryLayoutConstants.MetrologyTimestampOffset)
        {
            throw new InvalidOperationException(
                $"MetrologyData timestamp must start at {BinaryLayoutConstants.MetrologyTimestampOffset} bytes, actual {TimestampOffset}.");
        }

        if (MetrologyDataSize != BinaryLayoutConstants.MetrologyDataSize)
        {
            throw new InvalidOperationException($"MetrologyData size must be {BinaryLayoutConstants.MetrologyDataSize} bytes, actual {MetrologyDataSize}.");
        }
    }
}
