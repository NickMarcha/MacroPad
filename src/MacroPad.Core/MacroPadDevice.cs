using HidSharp;

namespace MacroPad.Core;

/// <summary>An open connection to the macropad's configuration interface.</summary>
public sealed class MacroPadDevice : IDisposable
{
    readonly HidStream _stream;

    public HidDevice Device { get; }

    /// <summary>What the device reports about itself (first bytes of the answer to the info command).</summary>
    public byte[] Info { get; }

    MacroPadDevice(HidDevice device, HidStream stream)
    {
        Device = device;
        _stream = stream;
        _stream.ReadTimeout = 500;
        _stream.WriteTimeout = 1000;
        Send(Protocol.Info());
        Info = Receive()[..8];
    }

    public string Name => $"{Device.VendorID:X4}:{Device.ProductID:X4}";

    /// <summary>Opens a pad's configuration interface (from <see cref="Pads.Connected"/>), or returns null.</summary>
    public static MacroPadDevice? Open(HidDevice device)
    {
        if (!device.TryOpen(out HidStream stream))
            return null;
        try
        {
            return new MacroPadDevice(device, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Reads every slot of one layer (1-based) from the device.</summary>
    public Dictionary<int, Binding> ReadLayer(int layer)
    {
        Send(Protocol.ReadLayer(layer));
        var result = new Dictionary<int, Binding>();
        for (int i = 0; i < Protocol.SlotCount; i++)
        {
            var (_, slot, binding) = Protocol.ParseSlot(Receive());
            result[slot] = binding;
        }
        return result;
    }

    /// <summary>Writes bindings for one layer (1-based) and saves them to the device's flash.</summary>
    public void WriteBindings(int layer, IEnumerable<KeyValuePair<int, Binding>> bindings)
    {
        foreach (var (slot, binding) in bindings)
            Send(Protocol.WriteBinding(layer, slot, binding));
        Commit();
    }

    public void WriteLed(int layer, LedMode mode, LedColor color)
    {
        Send(Protocol.WriteLed(layer, mode, color));
        Commit();
    }

    void Commit()
    {
        Send(Protocol.Commit());
        // The vendor app waits after committing; the pad is busy writing flash.
        Thread.Sleep(200);
    }

    void Send(byte[] packet) => _stream.Write(packet);

    byte[] Receive()
    {
        var buffer = new byte[Protocol.ReportLength];
        int n = _stream.Read(buffer, 0, buffer.Length);
        return buffer[..n];
    }

    public void Dispose() => _stream.Dispose();
}
