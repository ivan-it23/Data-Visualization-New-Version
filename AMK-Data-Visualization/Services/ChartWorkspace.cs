using AMK.DataVisualization.Charting.Configuration;
using AMK.DataVisualization.Charting.Series;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.IO.DataFiles;

namespace AMK.DataVisualization.App.Services;

internal enum Graph2DataMode
{
    LinkedToGraph1,
    IndependentFile
}

/// <summary>
/// Что показывает первый график: живые кадры опроса или открытый файл данных.
/// </summary>
/// <remarks>
/// Источники разделены, потому что они существуют одновременно. Опрос прибора
/// не прекращается оттого, что пользователь открыл файл: кадры продолжают
/// приходить и писаться на диск. Пока источник был один, эти кадры дописывались
/// в хвост открытого файла — на графике появлялась запись, которой в файле нет.
/// </remarks>
internal enum Graph1DataSource
{
    /// <summary>Кадры, принимаемые от прибора в текущей сессии опроса.</summary>
    Device,

    /// <summary>Файл данных, открытый пользователем.</summary>
    File
}

/// <summary>
/// Surface a graph's applied configuration came from. The curve-setup window only
/// highlights schematic elements for configurations it produced itself: a
/// selection made on the chart cannot be re-derived from the curves without
/// mis-highlighting elements the user never touched.
/// </summary>
internal enum ChartConfigurationSource
{
    CurveSetupWindow,
    OnChartOverlay,
    NoteSnapshot
}

/// <summary>
/// Owns the source-of-data contract for the two graph surfaces.
/// Graph 2 keeps its own repository, but reads the primary repository while
/// its legacy "Стандартный режим" is selected.
/// </summary>
internal sealed class ChartWorkspace
{
    public ChartWorkspace(ChartConfiguration graph1Configuration, ChartConfiguration graph2Configuration)
    {
        Graph1Configuration = graph1Configuration;
        Graph2Configuration = graph2Configuration;
    }

    /// <summary>
    /// Кадры текущей сессии опроса. Наполняется всегда, пока идёт опрос, — и
    /// тогда, когда на графике показан открытый файл, а не прибор.
    /// </summary>
    public ToolDataRepository Graph1DeviceRepository { get; } = new();

    /// <summary>
    /// Файл данных, открытый для первого графика. Живые кадры сюда не
    /// попадают: файл на диске от них не растёт, и график не должен показывать
    /// иное.
    /// </summary>
    public ToolDataRepository Graph1FileRepository { get; } = new();

    /// <summary>
    /// Хранилище, которое первый график показывает прямо сейчас.
    /// </summary>
    public ToolDataRepository Graph1Repository => Graph1Source == Graph1DataSource.Device
        ? Graph1DeviceRepository
        : Graph1FileRepository;

    /// <summary>
    /// Источник, показываемый первым графиком. По умолчанию файловый: до первой
    /// сессии опроса живых кадров не существует.
    /// </summary>
    public Graph1DataSource Graph1Source { get; private set; } = Graph1DataSource.File;

    public void SetGraph1Source(Graph1DataSource source)
    {
        Graph1Source = source;
    }

    public ToolDataRepository Graph2IndependentRepository { get; } = new();

    public ChartViewportState Graph1Viewport { get; } = new();

    public ChartViewportState Graph2Viewport { get; } = new();

    public ChartConfiguration Graph1Configuration { get; private set; }

    public ChartConfiguration Graph2Configuration { get; private set; }

    public Graph2DataMode Graph2Mode { get; private set; } = Graph2DataMode.LinkedToGraph1;

    private ChartConfigurationSource graph1ConfigurationSource = ChartConfigurationSource.CurveSetupWindow;
    private ChartConfigurationSource graph2ConfigurationSource = ChartConfigurationSource.CurveSetupWindow;

    public bool IsGraph2Independent => Graph2Mode == Graph2DataMode.IndependentFile;

    public void SetGraph2Mode(Graph2DataMode mode)
    {
        Graph2Mode = mode;
    }

    public ToolDataRepository GetRepositoryForGraph(int graphNumber)
    {
        return graphNumber switch
        {
            1 => Graph1Repository,
            2 when IsGraph2Independent => Graph2IndependentRepository,
            2 => Graph1Repository,
            _ => throw new ArgumentOutOfRangeException(nameof(graphNumber))
        };
    }

    public ChartConfiguration GetConfigurationForGraph(int graphNumber)
    {
        return graphNumber switch
        {
            1 => Graph1Configuration,
            2 => Graph2Configuration,
            _ => throw new ArgumentOutOfRangeException(nameof(graphNumber))
        };
    }

    public ChartViewportState GetViewportForGraph(int graphNumber)
    {
        return graphNumber switch
        {
            1 => Graph1Viewport,
            2 => Graph2Viewport,
            _ => throw new ArgumentOutOfRangeException(nameof(graphNumber))
        };
    }

    /// <summary>
    /// Number of transmitters the source loaded into the graph actually has.
    /// Both the schematic window and the on-chart schematic must ask here instead
    /// of assuming the full UI set, otherwise T5 stays selectable on a tool that
    /// has four transmitters (Картограф LWD 4tx).
    /// </summary>
    public int GetAvailableTransmitterCountForGraph(int graphNumber)
    {
        return TryGetSourceCapabilitiesForGraph(graphNumber, out ToolCapabilities capabilities)
            && capabilities.TransmitterCount > 0
            ? capabilities.TransmitterCount
            : SchematicSelection.UiTransmitterCount;
    }

    /// <summary>
    /// Источник графика записан прибором с новой прошивкой. Единственное место,
    /// где это выясняется: и меню пресетов, и окно схемы обязаны спрашивать
    /// здесь, а не читать метаданные самостоятельно.
    /// </summary>
    /// <remarks>
    /// Пустой график отвечает «нет»: пока источника не существует, включать
    /// пункты меню, которых у половины парка приборов нет, нельзя. Для удаления
    /// кривых этого ответа недостаточно — см.
    /// <see cref="TryGetSourceCapabilitiesForGraph"/>.
    /// </remarks>
    public bool IsExtendedFirmwareSourceForGraph(int graphNumber)
    {
        return TryGetSourceCapabilitiesForGraph(graphNumber, out ToolCapabilities capabilities)
            && capabilities.IsExtendedFirmware;
    }

    /// <summary>
    /// Возможности источника, загруженного в график. Возвращает <c>false</c>,
    /// когда источника ещё нет, и тогда о приборе не известно ничего.
    /// </summary>
    /// <remarks>
    /// Отличать «источник старой прошивки» от «источника пока нет» обязательно.
    /// Пустое хранилище — штатное состояние: оно наступает между нажатием
    /// «Включить» и первым принятым кадром, потому что сессия опроса начинается
    /// с очистки хранилища. Если считать это состояние старой прошивкой,
    /// подготовка сессии удалит из уже применённой конфигурации все амплитудные
    /// кривые, и пришедшие следом кадры новой прошивки рисовать будет нечего.
    /// </remarks>
    private bool TryGetSourceCapabilitiesForGraph(int graphNumber, out ToolCapabilities capabilities)
    {
        ToolDataRepository repository = GetRepositoryForGraph(graphNumber);
        if (repository.TryGetRecordMetadata(0, out ToolRecordMetadata metadata))
        {
            capabilities = metadata.Capabilities;
            return true;
        }

        capabilities = ToolCapabilities.Empty;
        return false;
    }

    /// <summary>
    /// Drops curves the loaded source cannot provide and reports whether anything
    /// was removed. A configuration built for a 5Tx tool survives a switch to a
    /// four-transmitter source, and its T5 curves would otherwise keep appearing
    /// in the legend, in the on-chart schematic and in the visibility list.
    /// </summary>
    public bool DropUnsupportedCurvesForGraph(int graphNumber)
    {
        ChartConfiguration configuration = GetConfigurationForGraph(graphNumber);
        if (configuration.IsEmpty)
        {
            return false;
        }

        int availableTransmitterCount = GetAvailableTransmitterCountForGraph(graphNumber);
        int removedCount = configuration.Curves.RemoveAll(curve =>
            curve.SourceElement == ElementType.Transmitter
            && (curve.ElementIndex < 0 || curve.ElementIndex >= availableTransmitterCount));

        // Конфигурация, построенная для прибора новой прошивки, могла пережить
        // переход на источник старой: её амплитудные кривые данных не имеют и
        // должны исчезнуть до того, как их прочитают легенда и список
        // видимости.
        //
        // Удаляем только тогда, когда источник есть и он назвал себя старым.
        // Пустое хранилище о приборе не говорит ничего, а удаление необратимо:
        // кривую, выброшенную из применённой конфигурации, обратно вернёт
        // только повторный выбор пресета пользователем.
        if (TryGetSourceCapabilitiesForGraph(graphNumber, out ToolCapabilities capabilities)
            && !capabilities.IsExtendedFirmware)
        {
            removedCount += configuration.Curves.RemoveAll(
                curve => curve.DataType is DataType.RhoAttSmt or DataType.AttDbSmt);
        }

        if (removedCount == 0)
        {
            return false;
        }

        configuration.UpdateAxisConfiguration();
        return true;
    }

    public ChartConfigurationSource GetConfigurationSourceForGraph(int graphNumber)
    {
        return graphNumber switch
        {
            1 => graph1ConfigurationSource,
            2 => graph2ConfigurationSource,
            _ => throw new ArgumentOutOfRangeException(nameof(graphNumber))
        };
    }

    public void ApplyConfigurationForGraph(
        int graphNumber,
        ChartConfiguration configuration,
        ChartConfigurationSource source)
    {
        ChartConfiguration copy = configuration.Copy();
        switch (graphNumber)
        {
            case 1:
                Graph1Configuration = copy;
                graph1ConfigurationSource = source;
                break;
            case 2:
                Graph2Configuration = copy;
                graph2ConfigurationSource = source;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(graphNumber));
        }
    }

    public string GetStatusFileInformation()
    {
        if (IsGraph2Independent)
        {
            return $"Г1: {DescribeRepository(Graph1Repository)} | Г2: {DescribeRepository(Graph2IndependentRepository)}";
        }

        return Graph1Repository.HasData
            ? $"Кадров: {Graph1Repository.DataSize}  |  {GetDisplayFileName(Graph1Repository)}"
            : "Файл не загружен";
    }

    private static string DescribeRepository(ToolDataRepository repository)
    {
        string fileName = repository.HasData ? GetDisplayFileName(repository) : "нет файла";
        return $"{fileName} ({repository.DataSize})";
    }

    private static string GetDisplayFileName(ToolDataRepository repository)
    {
        return string.IsNullOrWhiteSpace(repository.LoadedFileName)
            ? "нет файла"
            : Path.GetFileName(repository.LoadedFileName);
    }
}
