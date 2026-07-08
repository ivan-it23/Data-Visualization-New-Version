using System.Runtime.InteropServices;
using AMK.DataVisualization.Core;
using AMK.DataVisualization.Core.Adapters;
using AMK.DataVisualization.Core.Binary;

namespace AMK.DataVisualization.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        BinaryLayoutVerifier.ThrowIfInvalid();
        Assert(Marshal.SizeOf<GpToolData>() == BinaryLayoutConstants.GpToolDataSize, "Marshal.SizeOf<GpToolData>() mismatch.");
        Assert(Marshal.SizeOf<MetrologyData>() == BinaryLayoutConstants.MetrologyDataSize, "Marshal.SizeOf<MetrologyData>() mismatch.");

        var record = new GpToolData
        {
            Signature = 151_000,
            Condition = 7,
            Frame = 42,
            Temperature = 36.5f
        };
        record.SetDeltaPhase(1, 4, 123.25f);
        record.SetAmplitudeRx1(0, 2, 17.5f);
        record.SetZeroDeltaPhase(1, -2.5f);

        int size = Marshal.SizeOf<GpToolData>();
        byte[] bytes = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(record, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        bool parsed = ToolDataAdapterRegistry.Default.TryParseRawFrame(bytes, out ParsedToolFrame frame);
        Assert(parsed, "Universal adapter did not parse canonical frame.");
        Assert(frame.Kind == ToolDataKind.Autonom, "Expected Autonom kind for toolCode 151.");
        Assert(frame.Capabilities.TransmitterCount == 5, "Expected 5 transmitters for toolCode 151.");
        Assert(frame.Capabilities.HasT5, "Expected HasT5 for toolCode 151.");
        Assert(Math.Abs(frame.CanonicalData.GetDeltaPhase(1, 4) - 123.25f) < 0.0001f, "DeltaPhase accessor roundtrip failed.");
        Assert(Math.Abs(frame.CanonicalData.GetAmplitudeRx1(0, 2) - 17.5f) < 0.0001f, "AmplitudeRx1 accessor roundtrip failed.");
        Assert(Math.Abs(frame.CanonicalData.GetZeroDeltaPhase(1) + 2.5f) < 0.0001f, "ZeroDeltaPhase accessor roundtrip failed.");

        ToolIdentity identity = UniversalCanonicalAdapter.BuildIdentity(241_000);
        Assert(identity.DeviceType == DeviceType.Lwd, "Expected LWD device type for toolCode 241.");
        Assert(identity.TransmitterCount == 4, "Expected 4 transmitters for toolCode 241.");
        Assert(identity.Kind == ToolDataKind.Lwd, "Expected Lwd kind for toolCode 241.");

        Console.WriteLine("Core binary contract smoke tests passed.");
        Console.WriteLine($"GpToolData={BinaryLayoutVerifier.GpToolDataSize}; MetrologyData={BinaryLayoutVerifier.MetrologyDataSize}");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
