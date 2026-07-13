using AMK.DataVisualization.Charting.Series;
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

    public ChartConfiguration Graph1Configuration { get; }

    public ChartConfiguration Graph2Configuration { get; }

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
