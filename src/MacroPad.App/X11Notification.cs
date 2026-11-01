using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MacroPad.App;

/// <summary>
/// Marks a window as a notification for X11 window managers (KWin, Mutter, …), which then keep it above other windows,
/// leave it out of the taskbar and don't give it focus. Avalonia's own Topmost and ShowInTaskbar hints weren't enough
/// under KDE: the pop-up showed in the taskbar and went behind other windows. Avalonia has no notification window type,
/// so the property is set through Xlib on the window Avalonia created, before it is first shown (window managers read
/// the type when a window is mapped).
/// </summary>
public static class X11Notification
{
    const string LibX11 = "libX11.so.6";
    static readonly IntPtr XaAtom = 4;
    const int PropModeReplace = 0;

    /// <summary>Returns what it did, for the trace log.</summary>
    public static string Apply(Window window)
    {
        if (!OperatingSystem.IsLinux()) return "not Linux";
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "XID") return $"not an X11 window ({handle?.HandleDescriptor ?? "no handle"})";
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return "couldn't open the X display";
            try
            {
                IntPtr Atom(string name) => XInternAtom(display, name, false);
                XChangeProperty(display, handle.Handle, Atom("_NET_WM_WINDOW_TYPE"), XaAtom, 32, PropModeReplace,
                    [Atom("_NET_WM_WINDOW_TYPE_NOTIFICATION")], 1);
                IntPtr[] state = [Atom("_NET_WM_STATE_ABOVE"), Atom("_NET_WM_STATE_SKIP_TASKBAR"), Atom("_NET_WM_STATE_SKIP_PAGER")];
                XChangeProperty(display, handle.Handle, Atom("_NET_WM_STATE"), XaAtom, 32, PropModeReplace, state, state.Length);
                // Make sure the server has it before Avalonia maps the window on its own connection.
                XSync(display, false);
                return $"marked window 0x{handle.Handle:x} as a notification";
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return "libX11 not available: " + e.Message;
        }
    }

    [DllImport(LibX11)] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] static extern int XSync(IntPtr display, bool discard);

    // Format 32 properties are arrays of C longs, which are pointer-sized on 64-bit Linux.
    [DllImport(LibX11)]
    static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode,
        IntPtr[] data, int count);
}
