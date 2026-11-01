using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroPad.Core.Updates;

public sealed record ReleaseInfo(AppVersion Version, string Title, string Notes, string PageUrl, string? DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Checks the public GitHub releases and, for a portable copy, swaps the running single-file binary for the new one.
/// Release assets are named <c>MacroPad-win-x64.exe</c> and <c>MacroPad-linux-x64</c> (see .github/workflows/release.yml).
/// </summary>
public sealed class Updater(HttpClient http, string repo = Updater.Repository)
{
    public const string Repository = "NickMarcha/MacroPad";
    const string OldSuffix = ".old";
    const string NewSuffix = ".new";

    /// <summary>Passed to the updated binary with the old process's id; see <see cref="WaitForPreviousToExit"/>.</summary>
    public const string AfterUpdateArgument = "--after-update";

    public static string AssetName =>
        OperatingSystem.IsWindows() ? "MacroPad-win-x64.exe" : "MacroPad-linux-x64";

    /// <summary>The newest published release, or null when there is none yet.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MacroPad", AppVersion.Current.ToString()));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.SendAsync(request, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await JsonSerializer.DeserializeAsync(await response.Content.ReadAsStreamAsync(timeout.Token),
            UpdateJson.Default.GitHubRelease, timeout.Token);
        return release is null ? null : ToInfo(release);
    }

    public static ReleaseInfo? ToInfo(GitHubRelease release)
    {
        if (!AppVersion.TryParse(release.TagName, out var version)) return null;
        var asset = release.Assets.FirstOrDefault(a => a.Name == AssetName);
        var sha = asset?.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d[7..] : null;
        return new ReleaseInfo(version, release.Name ?? release.TagName, release.Body ?? "", release.HtmlUrl,
            asset?.DownloadUrl, asset?.Size ?? 0, sha);
    }

    /// <summary>
    /// Downloads the release beside the running binary, checks it, and puts it in the binary's place. The running file
    /// is renamed out of the way first (Windows allows renaming a running .exe), then deleted on the next start.
    /// Returns the path to start.
    /// </summary>
    public async Task<string> InstallAsync(ReleaseInfo release, string executable, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (release.DownloadUrl is null)
            throw new InvalidOperationException($"Release {release.Version} has no {AssetName}.");
        var next = executable + NewSuffix;
        using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var total = release.Size > 0 ? release.Size : response.Content.Headers.ContentLength ?? 0;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = File.Create(next);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                sha.AppendData(buffer, 0, read);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
            if (release.Size > 0 && done != release.Size)
                throw new IOException($"Download is {done:N0} bytes; the release lists {release.Size:N0}.");
            if (release.Sha256 is not null && !Convert.ToHexStringLower(sha.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The download's SHA-256 doesn't match the release.");
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(next, File.GetUnixFileMode(executable));
        var old = executable + OldSuffix;
        // A previous .old can still be in use (its process hasn't exited): step aside to a fresh name.
        if (File.Exists(old))
        {
            try { File.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { old = $"{old}-{Environment.TickCount64}"; }
        }
        File.Move(executable, old);
        try
        {
            File.Move(next, executable);
        }
        catch
        {
            File.Move(old, executable);
            throw;
        }
        return executable;
    }

    /// <summary>With <see cref="AfterUpdateArgument"/> &lt;pid&gt; in the arguments, waits for that process to end.</summary>
    public static void WaitForPreviousToExit(string[] args, TimeSpan timeout)
    {
        var i = Array.IndexOf(args, AfterUpdateArgument);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(timeout);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { }
    }

    /// <summary>Removes what previous updates left behind (exe.old, exe.old-123, exe.new). Call at start.</summary>
    public static void CleanUp(string? executable)
    {
        if (executable is null) return;
        var dir = Path.GetDirectoryName(executable)!;
        var name = Path.GetFileName(executable);
        IEnumerable<string> leftovers;
        try { leftovers = Directory.GetFiles(dir, name + OldSuffix + "*").Append(executable + NewSuffix); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (var leftover in leftovers)
        {
            try { File.Delete(leftover); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* still in use; next start */ }
        }
    }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    /// <summary>"sha256:&lt;hex&gt;", computed by GitHub for uploaded assets.</summary>
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal partial class UpdateJson : JsonSerializerContext;
