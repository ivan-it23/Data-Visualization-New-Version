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

    public ToolDataRepository Graph1Repository { get; } = new();

    public ToolDataRepository Graph2IndependentRepository { get; } = new();

    public ChartViewportState Graph1Viewport { get; } = new();

    public ChartViewportState Graph2Viewport { get; } = new();

    public ChartConfiguration Graph1Configuration { get; private set; }

    public ChartConfiguration Graph2Configuration { get; private set; }

    public Graph2DataMode Graph2Mode { get; private set; } = Graph2DataMode.LinkedToGraph1;

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
        ToolDataRepository repository = GetRepositoryForGraph(graphNumber);
        return repository.TryGetRecordMetadata(0, out ToolRecordMetadata metadata)
            && metadata.Capabilities.TransmitterCount > 0
            ? metadata.Capabilities.TransmitterCount
            : SchematicSelection.UiTransmitterCount;
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
        if (removedCount == 0)
        {
            return false;
        }

        configuration.UpdateAxisConfiguration();
        return true;
    }

    public void ApplyConfigurationForGraph(int graphNumber, ChartConfiguration configuration)
    {
        ChartConfiguration copy = configuration.Copy();
        switch (graphNumber)
        {
            case 1:
                Graph1Configuration = copy;
                break;
            case 2:
                Graph2Configuration = copy;
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
