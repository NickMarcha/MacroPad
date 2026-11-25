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

    /// <summary>How the pad is drawn: quarter turns clockwise from the vendor's layout (<see cref="PadLayout"/>).</summary>
    public int Rotation { get; set; }

    /// <summary>The pad's Bluetooth address, once MacroPad has linked it to this pad (<see cref="Pads.LinkBluetooth"/>).</summary>
    public string? Bluetooth { get; set; }

    /// <summary>The model ("514C:8850") when <see cref="Key"/> tells it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Model
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(Key, "^serial:([0-9A-F]{4}:[0-9A-F]{4}):|vid_([0-9a-f]{4})&pid_([0-9a-f]{4})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value.ToUpperInvariant() : $"{m.Groups[2].Value}:{m.Groups[3].Value}".ToUpperInvariant();
        }
    }

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

    /// <summary>The models that also work over Bluetooth.</summary>
    static readonly string[] BluetoothModels = ["514C:8850"];

    /// <summary>
    /// The pad each Bluetooth address belongs to. Over Bluetooth a pad shows neither its USB serial nor its model, so
    /// an address is linked to the one pad of a Bluetooth model that has none yet; with none or several of those, it
    /// gets a pad of its own. Sets <paramref name="changed"/> when it linked or added one, so the caller saves.
    /// </summary>
    public static Dictionary<string, PadRecord> LinkBluetooth(AppSettings settings, IEnumerable<string> addresses, out bool changed)
    {
        changed = false;
        var links = new Dictionary<string, PadRecord>();
        foreach (var address in addresses)
        {
            var record = settings.Pads.FirstOrDefault(p => p.Bluetooth == address);
            if (record is null)
            {
                var unlinked = settings.Pads.Where(p => p.Bluetooth is null && BluetoothModels.Contains(p.Model)).ToList();
                record = unlinked.Count == 1 ? unlinked[0] : settings.PadFor($"bluetooth:{address}");
                record.Bluetooth = address;
                changed = true;
            }
            links[address] = record;
        }
        return links;
    }

    static string? Serial(HidDevice d)
    {
        try { return d.GetSerialNumber(); }
        catch { return null; }
    }
}
