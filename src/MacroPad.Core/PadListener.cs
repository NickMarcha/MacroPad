using HidSharp;

namespace MacroPad.Core;

/// <summary>
/// Watches the pads' consumer-control reports (a 16-bit usage, then 0 on release) over USB and Bluetooth, and raises
/// <see cref="Pressed"/> for each press. Reading does not take the reports away from the OS. Copes with pads being
/// unplugged, switched off and back on.
/// </summary>
public sealed class PadListener : IDisposable
{
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
        var devices = PadIds.FindListenable().ToList();
        lock (_gate)
        {
            // A Bluetooth pad that is switched off can leave its read waiting on a dead handle (Windows); closing the
            // streams of devices that have gone ends those reads, so the pad opens afresh when it comes back.
            var present = devices.Select(d => d.DevicePath).ToHashSet();
            foreach (var (path, stream) in _open.Where(o => !present.Contains(o.Key)).ToList())
            {
                _open.Remove(path);
                stream.Dispose();
            }
        }
        foreach (var device in devices)
        {
            lock (_gate)
            {
                if (_open.ContainsKey(device.DevicePath)) continue;
                var reportId = PadIds.ConsumerReportId(device);
                if (reportId is null) continue;
                // Windows keeps keyboard and mouse collections to itself; those fail to open and are skipped.
                if (!device.TryOpen(out HidStream stream)) continue;
                stream.ReadTimeout = Timeout.Infinite;
                _open[device.DevicePath] = stream;
                new Thread(() => ReadLoop(device.DevicePath, stream, reportId.Value)) { IsBackground = true, Name = "MacroPad listener" }.Start();
            }
        }
    }

    void ReadLoop(string path, HidStream stream, byte reportId)
    {
        var buffer = new byte[Math.Max(64, stream.Device.GetMaxInputReportLength())];
        ushort last = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n < 3 || buffer[0] != reportId) continue;
                var usage = (ushort)(buffer[1] | buffer[2] << 8);
                if (usage != 0 && usage != last) Pressed?.Invoke(usage);
                last = usage;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException)
        {
            // Unplugged, switched off or closed; the device-list change when it comes back opens it again.
        }
        finally
        {
            // Only this stream's entry: a newer stream may already be open on the same path.
            lock (_gate)
                if (_open.TryGetValue(path, out var current) && current == stream) _open.Remove(path);
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

/// <summary>The USB IDs the vendor's MINI_KEYBOARD.exe looks for (514C:8851 and 514C:8850 tested), and the pads over Bluetooth.</summary>
public static class PadIds
{
    /// <summary>
    /// The product name a pad gives over Bluetooth, where it has no configuration interface and borrows Apple's IDs
    /// (05AC:022C on the 12-key pad). Windows reports no name while a paired pad is switched off, so the IDs count too;
    /// listening to a real Apple keyboard is harmless, as it never sends MacroPad's codes.
    /// </summary>
    public const string BluetoothName = "MINI_KEYBOARD";
    const int BluetoothVendorId = 0x05AC, BluetoothProductId = 0x022C;
    const uint ConsumerControl = 0x000C0001;

    static readonly int[] VendorIds = [0x514C, 0x1189];
    static readonly int[] ProductIds = [0x8842, 0x8840, 0x8830, 0x8831, 0x8832, 0x8833, 0x8850, 0x8851];

    public static IEnumerable<HidDevice> Find() => DeviceList.Local.GetHidDevices()
        .Where(d => VendorIds.Contains(d.VendorID) && ProductIds.Contains(d.ProductID));

    /// <summary>Everything a press can arrive on: the pads' USB key interfaces and pads paired over Bluetooth.</summary>
    public static IEnumerable<HidDevice> FindListenable() =>
        Find().Where(d => !IsConfigInterface(d)).Concat(DeviceList.Local.GetHidDevices().Where(IsBluetoothPad));

    /// <summary>
    /// The Bluetooth addresses (12 hex digits, lower case) of the pads connected over Bluetooth now. Windows lists a
    /// paired pad's HID collections only while it is switched on and connected.
    /// </summary>
    public static IReadOnlyList<string> BluetoothAddresses() => DeviceList.Local.GetHidDevices().Where(IsBluetoothPad)
        .Select(BluetoothAddress).OfType<string>().Distinct().ToList();

    static string? BluetoothAddress(HidDevice d)
    {
        // The pad gives its address as the serial number; the device path carries it too ("…_a1b2c3d4e5f6&col03…").
        string? serial = null;
        try { serial = d.GetSerialNumber(); }
        catch { }
        serial = serial?.Replace(":", "").Trim().ToLowerInvariant();
        if (serial is { Length: 12 } && serial.All(Uri.IsHexDigit)) return serial;
        var m = System.Text.RegularExpressions.Regex.Match(d.DevicePath, "_([0-9a-fA-F]{12})(?:&|#|$)");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    static bool IsBluetoothPad(HidDevice d)
    {
        if (d.VendorID == BluetoothVendorId && d.ProductID == BluetoothProductId) return true;
        try { return d.GetProductName() == BluetoothName; }
        catch { return false; }
    }

    /// <summary>
    /// The report ID that carries consumer-control usages on this device (5 over USB, 3 over Bluetooth on the 12-key pad),
    /// or null when it has none.
    /// </summary>
    public static byte? ConsumerReportId(HidDevice d)
    {
        try
        {
            // The Consumer Control collection; other collections can hold consumer items too (the mouse's horizontal wheel).
            var consumer = d.GetReportDescriptor().DeviceItems.FirstOrDefault(i => i.Usages.GetAllValues().Contains(ConsumerControl));
            return consumer?.InputReports.FirstOrDefault()?.ReportID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The configuration interface takes 65-byte output reports; the others are the keyboard, mouse and media keys.</summary>
    public static bool IsConfigInterface(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength() == Protocol.ReportLength; }
        catch { return false; }
    }
}
