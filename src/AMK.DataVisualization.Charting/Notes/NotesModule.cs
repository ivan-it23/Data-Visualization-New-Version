using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMK.DataVisualization.Charting.Series;

namespace AMK.DataVisualization.Charting.Notes;

/// <summary>
/// Append-only sidecar storage compatible with the legacy NotesModule.
/// </summary>
public sealed class NotesModule
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly List<ChartNote> notes = [];
    private string notesPath = string.Empty;
    private bool loaded;

    public string NotesPath => notesPath;

    public bool IsLoaded => loaded;

    public IReadOnlyList<ChartNote> GetAllNotes() => notes;

    public void SetNotesPath(string path)
    {
        path ??= string.Empty;
        if (string.Equals(notesPath, path, StringComparison.Ordinal))
        {
            return;
        }

        notesPath = path;
        loaded = false;
        notes.Clear();
    }

    public void Clear()
    {
        notes.Clear();
        loaded = false;
        notesPath = string.Empty;
    }

    public bool LoadFromDisk()
    {
        notes.Clear();
        loaded = true;

        if (notesPath.Length == 0 || !File.Exists(notesPath))
        {
            return true;
        }

        Dictionary<string, ChartNote> noteMap = new(StringComparer.Ordinal);
        try
        {
            foreach (string line in File.ReadLines(notesPath, Utf8WithoutBom))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (TryExtractString(line, "op", out string operation))
                {
                    if (operation == "delete")
                    {
                        if (TryExtractString(line, "noteId", out string deletedId) && deletedId.Length > 0)
                        {
                            noteMap.Remove(deletedId);
                        }

                        continue;
                    }

                    if (operation == "update")
                    {
                        if (TryExtractString(line, "noteId", out string updatedId)
                            && TryExtractString(line, "text", out string updatedText)
                            && updatedId.Length > 0
                            && noteMap.TryGetValue(updatedId, out ChartNote? existing))
                        {
                            existing.Text = updatedText;
                        }

                        continue;
                    }
                }

                ChartNote note = new();
                _ = TryExtractString(line, "noteId", out string noteId);
                note.NoteId = noteId;
                _ = TryExtractUInt64(line, "createdUtcMs", out ulong createdUtcMs);
                note.CreatedUtcMs = createdUtcMs;
                _ = TryExtractUInt64(line, "configKey", out ulong configKey);
                note.ConfigKey = configKey;
                _ = TryExtractString(line, "configSnapshot", out string configSnapshot);
                note.ConfigSnapshot = configSnapshot;
                _ = TryExtractString(line, "presetName", out string presetName);
                note.PresetName = presetName;

                if (TryExtractString(line, "xAxisType", out string xAxisType))
                {
                    note.XAxisType = xAxisType == "TEMPERATURE" ? XAxisType.Temperature : XAxisType.FrameNumber;
                }

                _ = TryExtractInt32(line, "xIndex", out int xIndex);
                note.XIndex = xIndex;
                _ = TryExtractDouble(line, "yLeft", out double yLeft);
                note.YLeft = yLeft;
                _ = TryExtractDouble(line, "yRight", out double yRight);
                note.YRight = yRight;
                _ = TryExtractBoolean(line, "hasYRight", out bool hasYRight);
                note.HasYRight = hasYRight;
                _ = TryExtractInt32(line, "anchorAxisGroup", out int anchorAxisGroup);
                note.AnchorAxisGroup = anchorAxisGroup;
                _ = TryExtractString(line, "text", out string text);
                note.Text = text;

                if (note.NoteId.Length > 0 && note.ConfigKey != 0)
                {
                    noteMap[note.NoteId] = note;
                }
            }
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        notes.AddRange(noteMap.Values.OrderBy(note => note.CreatedUtcMs));
        foreach (ChartNote note in notes)
        {
            if (note.ConfigSnapshot.Length == 0)
            {
                continue;
            }

            ulong migratedKey = RecomputeConfigKeyFromSnapshot(note.ConfigSnapshot);
            if (migratedKey != 0)
            {
                note.ConfigKey = migratedKey;
            }
        }

        return true;
    }

    public IReadOnlyList<ChartNote> GetNotesForConfig(ulong configKey)
    {
        return notes.Where(note => note.ConfigKey == configKey).ToList();
    }

    public bool AppendNote(ChartNote note)
    {
        if (notesPath.Length == 0)
        {
            return false;
        }

        string line = "{"
            + $"\"noteId\":\"{EscapeJsonString(note.NoteId)}\","
            + $"\"createdUtcMs\":{note.CreatedUtcMs},"
            + $"\"configKey\":{note.ConfigKey},"
            + $"\"configSnapshot\":\"{EscapeJsonString(note.ConfigSnapshot)}\","
            + $"\"presetName\":\"{EscapeJsonString(note.PresetName)}\","
            + $"\"xAxisType\":\"{(note.XAxisType == XAxisType.Temperature ? "TEMPERATURE" : "FRAME_NUMBER")}\","
            + $"\"xIndex\":{note.XIndex},"
            + $"\"yLeft\":{note.YLeft.ToString("F8", CultureInfo.InvariantCulture)},"
            + $"\"hasYRight\":{(note.HasYRight ? "true" : "false")},"
            + $"\"yRight\":{note.YRight.ToString("F8", CultureInfo.InvariantCulture)},"
            + $"\"anchorAxisGroup\":{note.AnchorAxisGroup},"
            + $"\"text\":\"{EscapeJsonString(note.Text)}\""
            + "}\n";

        if (!TryAppendLine(line))
        {
            return false;
        }

        notes.Add(note.Copy());
        return true;
    }

    public bool DeleteNoteById(string noteId)
    {
        if (noteId.Length == 0 || notesPath.Length == 0)
        {
            return false;
        }

        string line = "{"
            + "\"op\":\"delete\","
            + $"\"noteId\":\"{EscapeJsonString(noteId)}\","
            + $"\"deletedUtcMs\":{NowUtcMs()}"
            + "}\n";
        if (!TryAppendLine(line))
        {
            return false;
        }

        int index = notes.FindIndex(note => note.NoteId == noteId);
        if (index >= 0)
        {
            notes.RemoveAt(index);
        }

        return true;
    }

    public bool UpdateNoteTextById(string noteId, string newText)
    {
        if (noteId.Length == 0 || notesPath.Length == 0)
        {
            return false;
        }

        ChartNote? note = notes.FirstOrDefault(item => item.NoteId == noteId);
        if (note is null)
        {
            return false;
        }

        string line = "{"
            + "\"op\":\"update\","
            + $"\"noteId\":\"{EscapeJsonString(noteId)}\","
            + $"\"updatedUtcMs\":{NowUtcMs()},"
            + $"\"text\":\"{EscapeJsonString(newText)}\""
            + "}\n";
        if (!TryAppendLine(line))
        {
            return false;
        }

        note.Text = newText;
        return true;
    }

    public static ulong ComputeConfigKey(ChartConfiguration configuration)
    {
        string curves = BuildSortedCurveChunk(configuration.Curves.Where(curve => curve.Enabled)
            .Select(curve => new SnapshotCurveItem(curve.Id, curve.YAxisGroup)));
        string key = $"xAxisType={ToLegacyXAxis(configuration.XAxisType)};curves={curves}";
        return Fnv1a64(Utf8WithoutBom.GetBytes(key));
    }

    public static string BuildConfigSnapshot(ChartConfiguration configuration)
    {
        string curves = BuildSortedCurveChunk(configuration.Curves.Where(curve => curve.Enabled)
            .Select(curve => new SnapshotCurveItem(curve.Id, curve.YAxisGroup)));
        return $"xAxisType={ToLegacyXAxis(configuration.XAxisType)};activePreset={(int)configuration.ActivePreset};curves={curves}";
    }

    public static bool ParseConfigSnapshot(
        string snapshot,
        out XAxisType xAxisType,
        out PresetType preset,
        out List<SnapshotCurveItem> curves)
    {
        xAxisType = XAxisType.FrameNumber;
        preset = PresetType.None;
        curves = [];
        if (snapshot.Length == 0)
        {
            return false;
        }

        int xAxisPosition = snapshot.IndexOf("xAxisType=", StringComparison.Ordinal);
        if (xAxisPosition < 0)
        {
            return false;
        }

        int xAxisStart = xAxisPosition + "xAxisType=".Length;
        int xAxisEnd = snapshot.IndexOf(';', xAxisStart);
        if (xAxisEnd < 0)
        {
            xAxisEnd = snapshot.Length;
        }

        string xAxisValue = snapshot[xAxisStart..xAxisEnd];
        xAxisType = xAxisValue == "TEMPERATURE" ? XAxisType.Temperature : XAxisType.FrameNumber;

        int presetPosition = snapshot.IndexOf("activePreset=", StringComparison.Ordinal);
        if (presetPosition >= 0)
        {
            int presetStart = presetPosition + "activePreset=".Length;
            int presetEnd = snapshot.IndexOf(';', presetStart);
            if (presetEnd < 0)
            {
                presetEnd = snapshot.Length;
            }

            if (int.TryParse(snapshot[presetStart..presetEnd], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPreset)
                && parsedPreset >= (int)PresetType.None
                && parsedPreset <= (int)PresetType.Custom)
            {
                preset = (PresetType)parsedPreset;
            }
        }

        int curvesPosition = snapshot.IndexOf("curves=", StringComparison.Ordinal);
        if (curvesPosition < 0)
        {
            return true;
        }

        string curvesChunk = snapshot[(curvesPosition + "curves=".Length)..];
        if (curvesChunk.Length == 0)
        {
            return true;
        }

        foreach (string token in curvesChunk.Split(','))
        {
            int separator = token.LastIndexOf('@');
            if (separator <= 0 || separator + 1 >= token.Length)
            {
                continue;
            }

            if (int.TryParse(token[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axisGroup))
            {
                curves.Add(new SnapshotCurveItem(token[..separator], axisGroup == 1 ? 1 : 0));
            }
        }

        return true;
    }

    public static string GenerateNoteId()
    {
        Span<byte> randomBytes = stackalloc byte[sizeof(uint)];
        RandomNumberGenerator.Fill(randomBytes);
        uint suffix = BitConverter.ToUInt32(randomBytes);
        return $"{NowUtcMs()}_{suffix:x}";
    }

    public static ulong NowUtcMs()
    {
        return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static ulong RecomputeConfigKeyFromSnapshot(string snapshot)
    {
        if (!ParseConfigSnapshot(snapshot, out XAxisType xAxisType, out _, out List<SnapshotCurveItem> curves))
        {
            return 0;
        }

        string key = $"xAxisType={ToLegacyXAxis(xAxisType)};curves={BuildSortedCurveChunk(curves)}";
        return Fnv1a64(Utf8WithoutBom.GetBytes(key));
    }

    private static string BuildSortedCurveChunk(IEnumerable<SnapshotCurveItem> curves)
    {
        return string.Join(",", curves
            .OrderBy(item => item.CurveId, StringComparer.Ordinal)
            .ThenBy(item => item.YAxisGroup)
            .Select(item => $"{item.CurveId}@{item.YAxisGroup}"));
    }

    private static string ToLegacyXAxis(XAxisType xAxisType)
    {
        return xAxisType == XAxisType.Temperature ? "TEMPERATURE" : "FRAME_NUMBER";
    }

    private bool TryAppendLine(string line)
    {
        try
        {
            using FileStream stream = new(notesPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            byte[] bytes = Utf8WithoutBom.GetBytes(line);
            stream.Write(bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte value in data)
        {
            hash ^= value;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    private static string EscapeJsonString(string value)
    {
        StringBuilder escaped = new(value.Length + 16);
        foreach (char character in value)
        {
            escaped.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }

        return escaped.ToString();
    }

    private static bool TryExtractString(string line, string key, out string value)
    {
        value = string.Empty;
        if (!TryFindValue(line, key, out int position) || position >= line.Length || line[position] != '"')
        {
            return false;
        }

        position++;
        StringBuilder result = new();
        while (position < line.Length)
        {
            char character = line[position++];
            if (character == '"')
            {
                value = result.ToString();
                return true;
            }

            if (character == '\\')
            {
                if (position >= line.Length)
                {
                    return false;
                }

                char escaped = line[position++];
                result.Append(escaped switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });
            }
            else
            {
                result.Append(character);
            }
        }

        return false;
    }

    private static bool TryExtractUInt64(string line, string key, out ulong value)
    {
        value = 0;
        return TryExtractNumberToken(line, key, out string token)
            && ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractInt32(string line, string key, out int value)
    {
        value = 0;
        return TryExtractNumberToken(line, key, out string token)
            && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractDouble(string line, string key, out double value)
    {
        value = 0;
        return TryExtractNumberToken(line, key, out string token)
            && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractBoolean(string line, string key, out bool value)
    {
        value = false;
        if (!TryFindValue(line, key, out int position))
        {
            return false;
        }

        if (line.AsSpan(position).StartsWith("true".AsSpan(), StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        return line.AsSpan(position).StartsWith("false".AsSpan(), StringComparison.Ordinal);
    }

    private static bool TryExtractNumberToken(string line, string key, out string token)
    {
        token = string.Empty;
        if (!TryFindValue(line, key, out int position))
        {
            return false;
        }

        int end = position;
        while (end < line.Length && line[end] is not ',' and not '}' and not ' ' and not '\t' and not '\r' and not '\n')
        {
            end++;
        }

        if (end == position)
        {
            return false;
        }

        token = line[position..end];
        return true;
    }

    private static bool TryFindValue(string line, string key, out int position)
    {
        position = 0;
        int keyPosition = line.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (keyPosition < 0)
        {
            return false;
        }

        int colon = line.IndexOf(':', keyPosition + key.Length + 2);
        if (colon < 0)
        {
            return false;
        }

        position = colon + 1;
        while (position < line.Length && char.IsWhiteSpace(line[position]))
        {
            position++;
        }

        return true;
    }
}
