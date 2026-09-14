using AMK.DataVisualization.Charting.Series;

namespace AMK.DataVisualization.Charting.Configuration;

public enum DataTypeFilter
{
    All,
    AmplitudeOnly,
    PhaseOnly,
    RatioOnly,
    DifferenceOnly,
    TemperatureOnly,
    RhoSmtOnly,
    PhaseSmtOnly,

    /// <summary>Симметризованные затухания, только новая прошивка.</summary>
    AttDbSmtOnly,

    /// <summary>Амплитудные УЭС, только новая прошивка.</summary>
    RhoAttSmtOnly,

    /// <summary>
    /// Акселерометр из служебного заголовка записи `.DEV`. К схеме прибора
    /// отношения не имеет: ни передатчики, ни приёмники в выборе не участвуют.
    /// </summary>
    AccelerometerOnly
}

public sealed class SchematicSelection
{
    public const int UiTransmitterCount = 5;
    public const int UiReceiverCount = 2;
    public const int UiSilentCount = 2;

    public SchematicSelection(int availableTransmitterCount = UiTransmitterCount)
    {
        AvailableTransmitterCount = Math.Clamp(availableTransmitterCount, 1, UiTransmitterCount);
        Transmitters = new bool[AvailableTransmitterCount];
        Receivers = new bool[UiReceiverCount];
        SilentTransmitters = new bool[UiSilentCount];
    }

    public int AvailableTransmitterCount { get; }

    public bool[] Transmitters { get; }

    public bool[] Receivers { get; }

    public bool[] SilentTransmitters { get; }

    public FrequencyBand Frequency { get; set; } = FrequencyBand.Freq400KHz;

    public DataTypeFilter DataFilter { get; set; } = DataTypeFilter.All;

    public SmtComputationMode RhoSmtMode { get; set; } = SmtComputationMode.FromSonde;

    public SmtComputationMode PhaseSmtMode { get; set; } = SmtComputationMode.FromSonde;

    /// <summary>Режим расчёта амплитудного УЭС.</summary>
    public SmtComputationMode RhoAttSmtMode { get; set; } = SmtComputationMode.FromSonde;

    /// <summary>Режим расчёта симметризованного затухания.</summary>
    public SmtComputationMode AttDbSmtMode { get; set; } = SmtComputationMode.FromSonde;

    public int SelectedReceiverCount => Receivers.Count(selected => selected);

    public int SelectedTransmitterCount => Transmitters.Count(selected => selected);

    public int SelectedSilentCount => SilentTransmitters.Count(selected => selected);

    public bool IsValid => ValidationError.Length == 0;

    public string ValidationError
    {
        get
        {
            if (DataFilter != DataTypeFilter.All)
            {
                return string.Empty;
            }

            if (SelectedReceiverCount == 0 && SelectedTransmitterCount == 0 && SelectedSilentCount == 0)
            {
                return "Выберите элементы для построения графика";
            }

            if (SelectedTransmitterCount > 0 && SelectedReceiverCount > 0)
            {
                return "Нельзя выбирать передатчики и приёмники одновременно";
            }

            if (SelectedSilentCount > 0 && SelectedReceiverCount == UiReceiverCount)
            {
                return "Нельзя выбирать оба приёмника и молчащие передатчики одновременно";
            }

            return string.Empty;
        }
    }

    public bool SupportsTransmitter(int index)
    {
        return index >= 0 && index < AvailableTransmitterCount;
    }

    public void Clear()
    {
        Array.Fill(Transmitters, false);
        Array.Fill(Receivers, false);
        Array.Fill(SilentTransmitters, false);
        Frequency = FrequencyBand.Freq400KHz;
        DataFilter = DataTypeFilter.All;
        RhoSmtMode = SmtComputationMode.FromSonde;
        PhaseSmtMode = SmtComputationMode.FromSonde;
        RhoAttSmtMode = SmtComputationMode.FromSonde;
        AttDbSmtMode = SmtComputationMode.FromSonde;
    }

    public SchematicSelection Copy()
    {
        SchematicSelection copy = new(AvailableTransmitterCount)
        {
            Frequency = Frequency,
            DataFilter = DataFilter,
            RhoSmtMode = RhoSmtMode,
            PhaseSmtMode = PhaseSmtMode,
            RhoAttSmtMode = RhoAttSmtMode,
            AttDbSmtMode = AttDbSmtMode
        };
        Transmitters.CopyTo(copy.Transmitters, 0);
        Receivers.CopyTo(copy.Receivers, 0);
        SilentTransmitters.CopyTo(copy.SilentTransmitters, 0);
        return copy;
    }
}
