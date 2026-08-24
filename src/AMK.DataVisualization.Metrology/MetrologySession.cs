using System.Runtime.InteropServices;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Metrology;

public enum MetrologySource
{
    None,
    Device,
    File,
    DeviceWrite
}

public sealed class MetrologyChangedEventArgs(MetrologyData data, MetrologySource source) : EventArgs
{
    public MetrologyData Data { get; } = data;

    public MetrologySource Source { get; } = source;
}

public sealed class MetrologySession
{
    private MetrologyData current;

    public event EventHandler<MetrologyChangedEventArgs>? Changed;

    public bool HasData { get; private set; }

    public MetrologySource Source { get; private set; }

    public string CurrentFilePath { get; private set; } = string.Empty;

    public uint SelectedKey { get; private set; }

    public MetrologyData? Current => HasData ? current : null;

    public bool TryGet(out MetrologyData data)
    {
        data = current;
        return HasData;
    }

    public void Set(MetrologyData data, MetrologySource source, string filePath = "")
    {
        current = data;
        HasData = true;
        Source = source;
        CurrentFilePath = filePath ?? string.Empty;
        Changed?.Invoke(this, new MetrologyChangedEventArgs(data, source));
    }

    public void LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        MetrologyData data = MetrologyFile.Read(filePath);
        Set(data, MetrologySource.File, Path.GetFullPath(filePath));
    }

    public void SaveToFile(string filePath, MetrologyData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        MetrologyFile.Write(filePath, data);
        Set(data, MetrologySource.File, Path.GetFullPath(filePath));
    }

    public void SetSelectedKey(uint key)
    {
        SelectedKey = key;
    }
}

public static class MetrologyFile
{
    public static MetrologyData Read(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < BinaryLayoutConstants.MetrologyDataSize)
        {
            throw new InvalidDataException($"Файл метрологии должен содержать не менее {BinaryLayoutConstants.MetrologyDataSize} байт.");
        }

        byte[] bytes = new byte[BinaryLayoutConstants.MetrologyDataSize];
        stream.ReadExactly(bytes);
        MetrologyData data = MemoryMarshal.Read<MetrologyData>(bytes);
        if (!ToolDataAdapterRegistry.Default.IsSupportedSignature(data.Signature))
        {
            throw new InvalidDataException($"Неподдерживаемая сигнатура метрологии: 0x{data.Signature:X8}.");
        }

        return data;
    }

    public static void Write(string filePath, MetrologyData data)
    {
        byte[] bytes = new byte[BinaryLayoutConstants.MetrologyDataSize];
        MemoryMarshal.Write(bytes.AsSpan(), in data);
        File.WriteAllBytes(filePath, bytes);
    }
}

public readonly record struct MetrologySignatureInfo(
    uint Low20,
    int TypeClass,
    int TransmitterCount,
    int Modification,
    int Number,
    uint Palette,
    FirmwareGeneration Generation,
    int FrameSize);

public static class MetrologySignature
{
    public static MetrologySignatureInfo Decode(uint signature)
    {
        ToolSignatureInfo info = ToolSignature.Decode(signature);
        uint payload = (info.ToolCode * 1000u) + (uint)info.Number;
        return new MetrologySignatureInfo(
            payload,
            (int)(info.ToolCode / 100u),
            (int)((info.ToolCode / 10u) % 10u),
            (int)(info.ToolCode % 10u),
            info.Number,
            info.ToolCode,
            info.Generation,
            info.FrameSize);
    }

    /// <summary>
    /// Собирает сигнатуру из полей окна метрологии, сохраняя поколение
    /// прошивки исходной сигнатуры.
    /// </summary>
    /// <remarks>
    /// У новой прошивки размер структуры лежит в старших разрядах, у старой
    /// там мусор, который нужно сохранить как есть. Перепутать ветки нельзя:
    /// в первом случае прибор перестанет опознаваться, во втором в сигнатуру
    /// попадёт размер, которого прибор не отдаёт.
    /// </remarks>
    public static uint Build(
        int typeClass,
        int transmitterCount,
        int modification,
        int number,
        uint previousSignature)
    {
        int safeTypeClass = Math.Clamp(typeClass, 1, 3);
        int safeTransmitterCount = Math.Clamp(transmitterCount, 3, 5);
        int safeModification = Math.Clamp(modification, 1, 9);
        int safeNumber = Math.Clamp(number, 0, 999);
        uint toolCode = (uint)((safeTypeClass * 100) + (safeTransmitterCount * 10) + safeModification);

        ToolSignatureInfo previous = ToolSignature.Decode(previousSignature);
        if (previous.Generation == FirmwareGeneration.Extended)
        {
            return ToolSignature.Encode(previous.FrameSize, toolCode, safeNumber);
        }

        uint high12 = previousSignature & 0xFFF00000u;
        uint low20 = (toolCode * 1000u) + (uint)safeNumber;
        return high12 | (low20 & 0x000FFFFFu);
    }

    public static uint BuildAutonom5Tx(int modification, int number, uint previousSignature)
    {
        return Build(1, 5, modification, number, previousSignature);
    }

    public static string ToBinaryString(uint signature)
    {
        return Convert.ToString(signature, 2).PadLeft(32, '0');
    }
}
