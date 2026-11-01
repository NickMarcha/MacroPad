using System;
using System.IO;

namespace MacroPad.App;

/// <summary>A timestamped log for chasing timing problems (knob, pop-up). Only when MACROPAD_TRACE=1.</summary>
public static class Trace
{
    static readonly bool On = Environment.GetEnvironmentVariable("MACROPAD_TRACE") == "1";
    public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "macropad-trace.log");
    static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!On) return;
        lock (Gate) File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
    }
}
