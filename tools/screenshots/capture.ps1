<#
Screenshots for the README, made against stand-in services so no real home or audio setup shows.

  1. python tools/screenshots/fake_services.py            (leave running: fake Home Assistant :8123, Bragi :8765)
  2. dotnet run tools/screenshots/demo-settings.cs -- tools/screenshots/out/demo-settings.json
  3. dotnet publish src/MacroPad.App -c Release -r win-x64 -p:Version=9.9.9 -o tools/screenshots/out/app
  4. Quit your own MacroPad (single instance: a second one would hand over to it), then
     pwsh tools/screenshots/capture.ps1 windows     -> out/pad.png, out/settings.png
     pwsh tools/screenshots/capture.ps1 popup 60    -> out/popup-NN.png while you press pad keys
     pwsh tools/screenshots/capture.ps1 restore     -> your own settings back, demo app closed

Windows are captured with PrintWindow (the window draws itself), so nothing else on the screen can end up in them,
then cropped to the visible frame. The pop-up's rounded corners need masking afterwards (see README in this folder).
#>
param([string]$Step = 'windows', [int]$Seconds = 60)
$here = $PSScriptRoot
$out = Join-Path $here 'out'
$settings = Join-Path $env:APPDATA 'MacroPad\settings.json'
$backup = Join-Path $out 'your-settings-backup.json'
$exe = Join-Path $out 'app\MacroPad.exe'
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static RECT Bounds(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }
  // Visible top-level windows of a process narrower than 500 px: the pop-up.
  public static IntPtr[] Popups(int pid) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); RECT r;
      if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r) && r.R - r.L > 100 && r.R - r.L < 500) list.Add(h); return true; }, IntPtr.Zero);
    return list.ToArray();
  }
}
'@
function Snap([IntPtr]$h, [string]$file) {
  $w = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$w) | Out-Null
  $f = [Win]::Bounds($h)
  $full = New-Object System.Drawing.Bitmap ($w.R - $w.L), ($w.B - $w.T)
  $g = [System.Drawing.Graphics]::FromImage($full); $hdc = $g.GetHdc()
  [Win]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
  $crop = New-Object System.Drawing.Rectangle ($f.L - $w.L), ($f.T - $w.T), ($f.R - $f.L), ($f.B - $f.T)
  $full.Clone($crop, $full.PixelFormat).Save((Join-Path $out $file)); "saved $file"
}
function DemoApp { Get-Process MacroPad -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1 }
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]

switch ($Step) {
  'windows' {
    if (-not (Test-Path $backup)) { Copy-Item $settings $backup }
    Copy-Item (Join-Path $out 'demo-settings.json') $settings -Force
    DemoApp | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep 1
    $p = Start-Process $exe -PassThru; Start-Sleep 12; $p.Refresh()
    Snap $p.MainWindowHandle 'pad.png'
    $win = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))
    $tab = $win.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Settings')))
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 1
    Snap $p.MainWindowHandle 'settings.png'
  }
  'popup' {
    $p = DemoApp; $end = (Get-Date).AddSeconds($Seconds); $i = 0
    while ((Get-Date) -lt $end -and $i -lt 60) {
      foreach ($h in [Win]::Popups($p.Id)) { Snap $h ("popup-{0:D2}.png" -f $i) | Out-Null; $i++ }
      Start-Sleep -Milliseconds 150
    }
    "captured $i pop-up frames"
  }
  'restore' {
    DemoApp | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep 1
    if (Test-Path $backup) { Copy-Item $backup $settings -Force; Remove-Item $backup; "your settings are back" }
  }
}
