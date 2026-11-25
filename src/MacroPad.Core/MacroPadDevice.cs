using HidSharp;

namespace MacroPad.Core;

/// <summary>An open connection to the macropad's configuration interface.</summary>
public sealed class MacroPadDevice : IDisposable
{
    readonly HidStream _stream;

    public HidDevice Device { get; }

    public PadModel Model { get; }

    /// <summary>What the device reports about itself (first bytes of the answer to the info command).</summary>
    public byte[] Info { get; }

    MacroPadDevice(HidDevice device, HidStream stream)
    {
        Device = device;
        Model = PadModel.For(device.VendorID, device.ProductID);
        _stream = stream;
        _stream.ReadTimeout = 500;
        _stream.WriteTimeout = 1000;
        Send(Protocol.Info());
        Info = Receive()[..8];
    }

    public string Name => $"{Device.VendorID:X4}:{Device.ProductID:X4}";

    /// <summary>Keys on the pad, from the info reply (3 on the 3-key knob pad, 12 on the 12-key two-knob pad).</summary>
    public int Buttons => Info[2] is > 0 and <= Protocol.MaxButtons ? Info[2] : 3;

    /// <summary>Knobs on the pad, from the info reply.</summary>
    public int Knobs => Info[3] <= Protocol.MaxKnobs ? Info[3] : 1;

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
        int slots = Protocol.SlotCount(Knobs);
        Send(Protocol.ReadLayer(layer, slots));
        var result = new Dictionary<int, Binding>();
        for (int i = 0; i < slots; i++)
        {
            var report = Receive();
            var (_, slot, binding) = Model.Format == PacketFormat.Steps ? StepProtocol.ParseSlot(report) : Protocol.ParseSlot(report);
            result[slot] = binding;
        }
        return result;
    }

    /// <summary>Writes bindings for one layer (1-based) and saves them to the device's flash.</summary>
    public void WriteBindings(int layer, IEnumerable<KeyValuePair<int, Binding>> bindings)
    {
        foreach (var (slot, binding) in bindings)
            Send(Model.Format == PacketFormat.Steps
                ? StepProtocol.WriteBinding(layer, slot, binding)
                : Protocol.WriteBinding(layer, slot, binding));
        Commit();
    }

    /// <summary>One layer's lights, on pads with a colour per key.</summary>
    public KeyLights ReadKeyLights(int layer)
    {
        Send(StepProtocol.ReadLights(layer));
        return StepProtocol.ParseLights(Receive());
    }

    /// <summary>Sets one layer's lights on pads with a colour per key, and saves them.</summary>
    public void WriteKeyLights(int layer, KeyLights lights)
    {
        Send(StepProtocol.WriteLights(layer, lights));
        Commit();
    }

    public void WriteLed(int layer, LedMode mode, LedColor color)
    {
        if (Model.Lights != LightKind.PerLayer)
            throw new NotSupportedException("This pad sets a colour per key; use WriteKeyLights.");
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
