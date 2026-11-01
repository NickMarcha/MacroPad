using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MacroPad.App;

/// <summary>
/// A system-wide keyboard shortcut on Windows (RegisterHotKey on a thread of its own). Shortcuts read like
/// "Ctrl+Alt+P": Ctrl, Shift, Alt and Win, then a letter, a digit or F1–F24.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int WM_QUIT = 0x0012;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    readonly Thread _thread;
    uint _threadId;

    GlobalHotkey(uint modifiers, uint key, Action pressed, Action<bool> registered)
    {
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var ok = RegisterHotKey(IntPtr.Zero, 1, modifiers | MOD_NOREPEAT, key);
            registered(ok);
            if (!ok) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == WM_HOTKEY) pressed();
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "MacroPad hotkey" };
        _thread.Start();
    }

    /// <summary>
    /// Registers <paramref name="shortcut"/>; <paramref name="registered"/> reports whether Windows accepted it (another
    /// app may own it). Null when the text isn't a shortcut or this isn't Windows.
    /// </summary>
    public static GlobalHotkey? Register(string? shortcut, Action pressed, Action<bool> registered)
    {
        if (!OperatingSystem.IsWindows() || !TryParse(shortcut, out var modifiers, out var key)) return null;
        return new GlobalHotkey(modifiers, key, pressed, registered);
    }

    public static bool IsValid(string? shortcut) => TryParse(shortcut, out _, out _);

    static bool TryParse(string? shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var parts = new List<string>(shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (parts.Count == 0) return false;
        var name = parts[^1].ToUpperInvariant();
        parts.RemoveAt(parts.Count - 1);
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": modifiers |= MOD_CONTROL; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "win": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }
        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') key = name[0];
        else if (name.StartsWith('F') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
        else return false;
        // A bare letter or digit would take that key from every app; F13–F24 alone are fine.
        return modifiers != 0 || key >= 0x7C;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
