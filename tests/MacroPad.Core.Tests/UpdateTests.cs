using System.Net;
using System.Security.Cryptography;
using MacroPad.Core.Updates;

namespace MacroPad.Core.Tests;

public class UpdateTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("macropad-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("0.4.0+abc123", 0, 4, 0)]
    [InlineData("2.0.1-beta", 2, 0, 1)]
    public void Parses_versions(string text, int a, int b, int c)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(a, b, c), v);
    }

    [Fact]
    public void Compares_versions()
    {
        Assert.True(new AppVersion(0, 10, 0) > new AppVersion(0, 9, 9));
        Assert.False(new AppVersion(1, 0, 0) > new AppVersion(1, 0, 0));
        Assert.True(default(AppVersion).IsDevelopment);
    }

    [Fact]
    public void Package_marker_means_package_managed()
    {
        var exe = Path.Combine(_dir, "MacroPad");
        File.WriteAllText(exe, "");
        File.WriteAllText(Path.Combine(_dir, InstallInfo.PackageManagedFile), "pacman (the AUR package macropad-bin)\n");
        var info = InstallInfo.Detect(exe, new AppVersion(0, 1, 0));
        Assert.Equal(InstallKind.PackageManaged, info.Kind);
        Assert.False(info.CanSelfUpdate);
        Assert.Contains("pacman", info.Describe());
    }

    [Fact]
    public void Writable_folder_means_portable_and_dev_build_never_updates()
    {
        var exe = Path.Combine(_dir, "MacroPad");
        Assert.Equal(InstallKind.Portable, InstallInfo.Detect(exe, new AppVersion(0, 1, 0)).Kind);
        Assert.Equal(InstallKind.Development, InstallInfo.Detect(exe, default).Kind);
        Assert.Equal(InstallKind.Development, InstallInfo.Detect(Path.Combine(_dir, "dotnet.exe"), new AppVersion(0, 1, 0)).Kind);
    }

    [Fact]
    public void Picks_this_platforms_asset()
    {
        var release = new GitHubRelease
        {
            TagName = "v0.2.0",
            HtmlUrl = "https://example.invalid/r",
            Assets =
            [
                new() { Name = "MacroPad-win-x64.exe", Size = 1, DownloadUrl = "https://example.invalid/w", Digest = "sha256:aa" },
                new() { Name = "MacroPad-linux-x64", Size = 2, DownloadUrl = "https://example.invalid/l", Digest = "sha256:bb" },
            ],
        };
        var info = Updater.ToInfo(release)!;
        Assert.Equal(new AppVersion(0, 2, 0), info.Version);
        Assert.Equal(OperatingSystem.IsWindows() ? "https://example.invalid/w" : "https://example.invalid/l", info.DownloadUrl);
    }

    [Fact]
    public async Task Install_swaps_the_binary_and_checks_the_hash()
    {
        var exe = Path.Combine(_dir, "MacroPad.exe");
        File.WriteAllText(exe, "old");
        var payload = "new version"u8.ToArray();
        var http = new HttpClient(new Fixed(payload));
        var updater = new Updater(http);
        var release = new ReleaseInfo(new AppVersion(9, 0, 0), "", "", "", "https://example.invalid/bin", payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(payload)));

        await updater.InstallAsync(release, exe);
        Assert.Equal("new version", File.ReadAllText(exe));
        Updater.CleanUp(exe);
        Assert.False(File.Exists(exe + ".old"));

        var bad = release with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<IOException>(() => updater.InstallAsync(bad, exe));
        Assert.Equal("new version", File.ReadAllText(exe));
    }

    sealed class Fixed(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
