using HidSharp;

namespace MacroPad.Core;

/// <summary>
/// Watches the pad's consumer-control reports (report ID 5: a 16-bit usage, then 0 on release) and raises
/// <see cref="Pressed"/> for each press. Reading does not take the reports away from the OS. Copes with the pad being
/// unplugged and plugged in again.
/// </summary>
public sealed class PadListener : IDisposable
{
    const byte ConsumerReportId = 5;
    readonly CancellationTokenSource _stop = new();
    readonly object _gate = new();
    readonly Dictionary<string, HidStream> _open = [];

    public event Action<ushort>? Pressed;

    public void Start()
    {
        DeviceList.Local.Changed += OnDevicesChanged;
        Scan();
    }

    void OnDevicesChanged(object? sender, DeviceListChangedEventArgs e) => Scan();

    void Scan()
    {
        if (_stop.IsCancellationRequested) return;
        foreach (var device in PadIds.Find().Where(d => !PadIds.IsConfigInterface(d)))
        {
            lock (_gate)
            {
                if (_open.ContainsKey(device.DevicePath)) continue;
                // Windows keeps keyboard and mouse collections to itself; those fail to open and are skipped.
                if (!device.TryOpen(out HidStream stream)) continue;
                stream.ReadTimeout = Timeout.Infinite;
                _open[device.DevicePath] = stream;
                new Thread(() => ReadLoop(device.DevicePath, stream)) { IsBackground = true, Name = "MacroPad listener" }.Start();
            }
        }
    }

    void ReadLoop(string path, HidStream stream)
    {
        var buffer = new byte[Math.Max(64, stream.Device.GetMaxInputReportLength())];
        ushort last = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n < 3 || buffer[0] != ConsumerReportId) continue;
                var usage = (ushort)(buffer[1] | buffer[2] << 8);
                if (usage != 0 && usage != last) Pressed?.Invoke(usage);
                last = usage;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException)
        {
            // Unplugged or closed; a later plug-in event opens it again.
        }
        finally
        {
            lock (_gate) _open.Remove(path);
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        DeviceList.Local.Changed -= OnDevicesChanged;
        lock (_gate)
        {
            foreach (var s in _open.Values) s.Dispose();
            _open.Clear();
        }
    }
}

/// <summary>The USB IDs the vendor's MINI_KEYBOARD.exe looks for. Only 514C:8851 has been tested.</summary>
public static class PadIds
{
    static readonly int[] VendorIds = [0x514C, 0x1189];
    static readonly int[] ProductIds = [0x8842, 0x8840, 0x8830, 0x8831, 0x8832, 0x8833, 0x8850, 0x8851];

    public static IEnumerable<HidDevice> Find() => DeviceList.Local.GetHidDevices()
        .Where(d => VendorIds.Contains(d.VendorID) && ProductIds.Contains(d.ProductID));

    /// <summary>The configuration interface takes 65-byte output reports; the others are the keyboard, mouse and media keys.</summary>
    public static bool IsConfigInterface(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength() == Protocol.ReportLength; }
        catch { return false; }
    }
}
