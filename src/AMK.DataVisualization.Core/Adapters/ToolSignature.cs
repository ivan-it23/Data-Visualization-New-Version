using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Core.Adapters;

/// <summary>
/// Поколение прошивки прибора. Определяется исключительно сигнатурой.
/// </summary>
public enum FirmwareGeneration
{
    /// <summary>
    /// Прошивка без явного размера структуры в сигнатуре. Кадр измерений —
    /// 240 байт, служебная область метрологии не расшифрована.
    /// </summary>
    Legacy,

    /// <summary>
    /// Прошивка, кодирующая размер структуры в старших разрядах сигнатуры.
    /// Кадр измерений — 320 байт, в метрологии появились воздушные поправки
    /// затухания.
    /// </summary>
    Extended
}

/// <summary>
/// Разобранная сигнатура прибора.
/// </summary>
/// <param name="Signature">Исходное значение сигнатуры.</param>
/// <param name="Generation">Поколение прошивки.</param>
/// <param name="FrameSize">Физический размер кадра измерений источника.</param>
/// <param name="ToolCode">Трёхзначный код прибора: тип, число передатчиков, модификация.</param>
/// <param name="Number">
/// Серийный номер прибора — поле «Серийный №» окна метрологии и `serial_number`
/// карточки прибора в базе учёта. Не путать с номером корпуса, который лежит по
/// смещению 4 структуры метрологии.
/// </param>
public readonly record struct ToolSignatureInfo(
    uint Signature,
    FirmwareGeneration Generation,
    int FrameSize,
    uint ToolCode,
    int Number);

/// <summary>
/// Единственное место, где сигнатура прибора превращается в код прибора, номер
/// и размер кадра.
/// </summary>
/// <remarks>
/// Схема кодирования взята из отладочного проекта разработчика новой прошивки
/// (<c>get_sonde_id</c> / <c>get_signature</c>):
/// <code>
/// сигнатура = размер_структуры * 1 000 000
///           + тип           *   100 000
///           + число_Tx      *    10 000
///           + модификация   *     1 000
///           + номер
/// </code>
/// Признак поколения — старшие разряды: если <c>сигнатура / 1 000 000 == 0</c>,
/// прибор работает под старой прошивкой и размер кадра выводится из типа.
///
/// Отличие от отладочного проекта одно и оно обязательно. Там разбор идёт
/// десятичной арифметикой по всему <c>uint32</c>, и на реальных сигнатурах
/// старых приборов вида <c>0x3143ADD1</c> (826 518 993) это даёт «размер 826,
/// тип 518» — мусор. Такие сигнатуры несут полезные данные только в младших
/// 20 битах, поэтому ветка старой прошивки здесь сохраняет маскирование,
/// которое уже работает на приборах в эксплуатации.
///
/// Перебором проверено: ни одна из 12 000 правдоподобных сигнатур новой
/// прошивки (размеры 240 и 320, коды приборов 151/152/231/241/242/349, номера
/// 0…999) не разбирается старой веткой как поддерживаемый прибор. Обратное
/// пересечение существует, поэтому размер кадра, полученный из сигнатуры,
/// обязан подтверждаться фактической длиной записи — см. <c>ToolDataFileReader</c>
/// и <c>SerialDeviceService</c>.
/// </remarks>
public static class ToolSignature
{
    /// <summary>Делитель, отделяющий размер структуры от остальных разрядов.</summary>
    private const uint FrameSizeScale = 1_000_000u;

    /// <summary>Разряды, занятые кодом прибора и номером.</summary>
    private const uint ToolCodeScale = 1_000u;

    /// <summary>Маска младших 20 бит для сигнатур старой прошивки.</summary>
    private const uint LegacyPayloadMask = 0x000FFFFFu;

    /// <summary>
    /// Размеры кадра, которые приложение умеет читать. Список расширяется
    /// только вместе с поддержкой соответствующей структуры данных.
    /// </summary>
    public static ReadOnlySpan<int> KnownFrameSizes =>
    [
        BinaryLayoutConstants.LegacyFrameSize,
        BinaryLayoutConstants.ExtendedFrameSize
    ];

    /// <summary>
    /// Разбирает сигнатуру. Возвращает <c>false</c>, если прибор не поддержан
    /// ни одной из веток.
    /// </summary>
    public static bool TryDecode(uint signature, out ToolSignatureInfo info)
    {
        if (TryDecodeExtended(signature, out info))
        {
            return true;
        }

        return TryDecodeLegacy(signature, out info);
    }

    /// <summary>
    /// Разбирает сигнатуру, не проверяя поддержку прибора. Нужна там, где
    /// неподдерживаемый прибор всё равно нужно назвать пользователю.
    /// </summary>
    public static ToolSignatureInfo Decode(uint signature)
    {
        if (TryDecode(signature, out ToolSignatureInfo info))
        {
            return info;
        }

        uint payload = signature & LegacyPayloadMask;
        return new ToolSignatureInfo(
            signature,
            FirmwareGeneration.Legacy,
            BinaryLayoutConstants.LegacyFrameSize,
            payload / ToolCodeScale,
            (int)(payload % ToolCodeScale));
    }

    /// <summary>
    /// Код прибора: тип * 100 + число передатчиков * 10 + модификация.
    /// </summary>
    public static uint DecodeToolCode(uint signature)
    {
        return Decode(signature).ToolCode;
    }

    /// <summary>
    /// Серийный номер прибора из сигнатуры.
    /// </summary>
    public static int DecodeNumber(uint signature)
    {
        return Decode(signature).Number;
    }

    /// <summary>
    /// Физический размер кадра измерений, который отдаёт этот прибор.
    /// </summary>
    public static int DecodeFrameSize(uint signature)
    {
        return Decode(signature).FrameSize;
    }

    /// <summary>
    /// Поколение прошивки прибора.
    /// </summary>
    public static FirmwareGeneration DecodeGeneration(uint signature)
    {
        return Decode(signature).Generation;
    }

    /// <summary>
    /// Собирает сигнатуру обратно. Используется только там, где размер кадра
    /// меняется осознанно; сами кадры приложение не переписывает.
    /// </summary>
    public static uint Encode(int frameSize, uint toolCode, int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 999);
        if (!IsKnownFrameSize((uint)frameSize))
        {
            throw new ArgumentOutOfRangeException(nameof(frameSize), frameSize, "Неизвестный размер кадра.");
        }

        return ((uint)frameSize * FrameSizeScale) + (toolCode * ToolCodeScale) + (uint)number;
    }

    public static bool IsKnownFrameSize(uint frameSize)
    {
        foreach (int knownFrameSize in KnownFrameSizes)
        {
            if (frameSize == (uint)knownFrameSize)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeExtended(uint signature, out ToolSignatureInfo info)
    {
        info = default;
        uint frameSize = signature / FrameSizeScale;

        // Признак новой прошивки по отладочному проекту: старшие разряды не
        // нулевые. Дополнительно требуем, чтобы там стоял известный размер
        // кадра, — иначе сигнатура старого прибора с мусором в старших битах
        // была бы принята за новую.
        if (frameSize == 0 || !IsKnownFrameSize(frameSize))
        {
            return false;
        }

        uint payload = signature % FrameSizeScale;
        uint toolCode = payload / ToolCodeScale;
        if (!UniversalCanonicalAdapter.IsSupportedToolCode(toolCode))
        {
            return false;
        }

        info = new ToolSignatureInfo(
            signature,
            FirmwareGeneration.Extended,
            (int)frameSize,
            toolCode,
            (int)(payload % ToolCodeScale));
        return true;
    }

    private static bool TryDecodeLegacy(uint signature, out ToolSignatureInfo info)
    {
        info = default;
        uint payload = signature & LegacyPayloadMask;
        uint toolCode = payload / ToolCodeScale;
        if (!UniversalCanonicalAdapter.IsSupportedToolCode(toolCode))
        {
            return false;
        }

        info = new ToolSignatureInfo(
            signature,
            FirmwareGeneration.Legacy,
            BinaryLayoutConstants.LegacyFrameSize,
            toolCode,
            (int)(payload % ToolCodeScale));
        return true;
    }
}
