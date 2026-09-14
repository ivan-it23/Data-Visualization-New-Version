using System.Buffers.Binary;

namespace AMK.DataVisualization.Core.Binary;

/// <summary>
/// Служебный заголовок записи файла `.DEV` — 11 байт перед кадром измерений.
/// </summary>
/// <remarks>
/// Заголовок пишет не та прошивка, что отвечает на команды обмена: ни в
/// `main_LWD_1DDS`, ни в `main_AUTO_5Tx.txt`, ни в `LWD_1_DDS_ATT_UES.c` нет
/// ни акселерометра, ни записи дампа памяти. Раскладка взята из отладочного
/// проекта разработчика прибора (`struct PREAMBULA`) и подтверждена разбором
/// реального файла: модуль вектора <c>(X, Y, Z)</c> держится постоянным при
/// неподвижном приборе и перетекает между осями при поворотах, <c>RamTime</c>
/// растёт ровно на единицу на каждой записи.
///
/// Единицы величин не подтверждены. <c>X</c>, <c>Y</c>, <c>Z</c> выводятся в
/// отсчётах: цена деления известна только по нашей оценке, и подписывать ось в
/// <c>g</c> означало бы выдать её за факт. <c>RamTime</c> и <c>T</c> на график
/// не выводятся по той же причине.
///
/// Заголовок есть у каждой записи `.DEV` обоих поколений прошивки и всегда
/// занимает <see cref="BinaryLayoutConstants.DevHeaderSize"/> байт.
/// </remarks>
/// <param name="RamTime">Счётчик записи. На реальном файле растёт на единицу.</param>
/// <param name="X">Первая ось акселерометра, отсчёты.</param>
/// <param name="Y">Вторая ось акселерометра, отсчёты.</param>
/// <param name="Z">Третья ось акселерометра, отсчёты.</param>
/// <param name="T">Температура, целые градусы. Датчик и единицы не подтверждены.</param>
public readonly record struct DevPreamble(int RamTime, short X, short Y, short Z, sbyte T)
{
    /// <summary>Модуль вектора ускорения в тех же отсчётах, что и компоненты.</summary>
    public float Magnitude => MathF.Sqrt(((float)X * X) + ((float)Y * Y) + ((float)Z * Z));

    /// <summary>
    /// Разбирает заголовок. Порядок байтов little-endian, как и во всех
    /// остальных структурах прибора.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out DevPreamble preamble)
    {
        preamble = default;
        if (source.Length < BinaryLayoutConstants.DevHeaderSize)
        {
            return false;
        }

        preamble = new DevPreamble(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            BinaryPrimitives.ReadInt16LittleEndian(source[4..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[6..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[8..]),
            (sbyte)source[10]);
        return true;
    }
}

/// <summary>
/// Сводка по служебным заголовкам открытого файла.
/// </summary>
/// <remarks>
/// <c>RamTime</c> и <c>T</c> на график не выводятся: первый — монотонный
/// счётчик, кривая из него была бы прямой линией, у второго не подтверждены
/// единицы. Оба полезны как диагностика, поэтому попадают в журнал сводкой по
/// файлу, а не построчно: записей в дампе тысячи, и журнал они бы вытеснили.
/// </remarks>
/// <param name="RecordCount">Записей с прочитанным заголовком.</param>
/// <param name="FirstRamTime">Счётчик первой записи.</param>
/// <param name="LastRamTime">Счётчик последней записи.</param>
/// <param name="RamTimeGapCount">
/// Переходы, где счётчик вырос не на единицу. Ненулевое значение означает, что
/// записи в дампе потеряны.
/// </param>
/// <param name="MinTemperature">Минимум поля <c>T</c> по файлу.</param>
/// <param name="MaxTemperature">Максимум поля <c>T</c> по файлу.</param>
public readonly record struct DevPreambleSummary(
    int RecordCount,
    int FirstRamTime,
    int LastRamTime,
    int RamTimeGapCount,
    sbyte MinTemperature,
    sbyte MaxTemperature);
