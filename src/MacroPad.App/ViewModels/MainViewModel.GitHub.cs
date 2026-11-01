using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;

namespace MacroPad.App.ViewModels;

/// <summary>Links to the project on GitHub: issue forms (.github/ISSUE_TEMPLATE) filled in with what MacroPad knows.</summary>
public partial class MainViewModel
{
    const string Repo = "https://github.com/NickMarcha/MacroPad";

    string OsText => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {Install.Describe()}";

    string PadText => _device is { } d
        ? $"{d.Name}, info {Convert.ToHexString(d.Info)}"
        : "none connected";

    [RelayCommand]
    void ReportProblem() => OpenIssue("bug_report.yml", ("version", VersionText), ("os", OsText), ("pad", PadText));

    [RelayCommand]
    void RequestConnection() => OpenIssue("connection_request.yml", ("version", VersionText), ("os", OsText));

    [RelayCommand]
    void RequestHardware() => OpenIssue("hardware_support.yml", ("version", VersionText), ("os", OsText), ("pad", PadText));

    [RelayCommand]
    void OpenGitHub() => Open(Repo);

    /// <summary>GitHub fills an issue form's fields from query parameters named after their ids.</summary>
    void OpenIssue(string template, params (string Field, string Value)[] fields)
    {
        var url = $"{Repo}/issues/new?template={template}";
        foreach (var (field, value) in fields) url += $"&{field}={Uri.EscapeDataString(value)}";
        Open(url);
    }

    void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            Message = $"Couldn't open the browser: {e.Message}. The page is {url}";
        }
    }
}
