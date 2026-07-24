namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Persisted per-graph preferences for the on-chart curve-selection overlays
/// (quick-preset dropdown, top-of-chart schematic and frequency dropdown).
/// Stored on disk so each graph restores its chosen layout on the next launch.
/// </summary>
public sealed class ChartDisplaySettings
{
    public GraphDisplayOptions Graph1 { get; set; } = new();

    public GraphDisplayOptions Graph2 { get; set; } = new();

    public GraphDisplayOptions ForGraph(int graphNumber)
    {
        return graphNumber == 2 ? Graph2 : Graph1;
    }
}

public sealed class GraphDisplayOptions
{
    public bool ShowPresetDropdown { get; set; }

    public bool ShowTopSchematic { get; set; }

    public bool ShowFrequencyDropdown { get; set; }

    /// <summary>The "eye" curve-visibility toggle follows the preset dropdown and
    /// the schematic, but not the frequency dropdown.</summary>
    public bool ShowEye => ShowPresetDropdown || ShowTopSchematic;

    /// <summary>Any overlay occupying the top row, used to reserve space above the
    /// legend.</summary>
    public bool AnyTopRow => ShowPresetDropdown || ShowTopSchematic || ShowFrequencyDropdown;
}
