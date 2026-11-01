using HidSharp;

namespace MacroPad.Core;

/// <summary>A pad MacroPad has seen: how it is recognised, and the number that picks its block of trigger codes.</summary>
public sealed class PadRecord
{
    /// <summary>The pad's USB serial number, or its device path when that doesn't tell it apart (see <see cref="Pads"/>).</summary>
    public string Key { get; set; } = "";

    public int Number { get; set; }

    /// <summary>A name you give it; null shows "Pad 1", "Pad 2", ….</summary>
    public string? Name { get; set; }

    public string Label => Name ?? $"Pad {Number + 1}";
}

/// <summary>A pad that is plugged in now: its configuration interface and the key it is known by.</summary>
public sealed record ConnectedPad(HidDevice Device, string Key)
{
    public string Model => $"{Device.VendorID:X4}:{Device.ProductID:X4}";
}

public static class Pads
{
    /// <summary>
    /// The pads plugged in now. A pad is known by its USB serial number, which follows it to any port. Cheap pads of one
    /// batch can share a serial (or have none); those are known by their device path instead, which follows the port.
    /// </summary>
    public static List<ConnectedPad> Connected()
    {
        var configs = PadIds.Find().Where(PadIds.IsConfigInterface).ToList();
        var serials = configs.Select(Serial).ToList();
        return configs.Select((d, i) =>
        {
            var serial = serials[i];
            var unique = !string.IsNullOrEmpty(serial) && serials.Count(s => s == serial) == 1;
            return new ConnectedPad(d, unique ? $"serial:{d.VendorID:X4}:{d.ProductID:X4}:{serial}" : $"path:{d.DevicePath}");
        }).ToList();
    }

    static string? Serial(HidDevice d)
    {
        try { return d.GetSerialNumber(); }
        catch { return null; }
    }
}
