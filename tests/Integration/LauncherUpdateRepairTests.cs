using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherUpdateRepairTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlUpdateRepair", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Prowl.Launcher.exe", false)]
    [InlineData("Prowl.Launcher.dll", false)]
    [InlineData("Prowl.Launcher.dll", true)]
    [InlineData("launcher-installation.json", false)]
    public async Task RetryingADamagedUpdateDownloadsAndRepairsIt(string file, bool delete)
    {
        using Fixture fixture = new(_home);
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        string damaged = Path.Combine(file == "launcher-installation.json"
            ? Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName : Path.GetDirectoryName(executable)!, file);
        if (delete) File.Delete(damaged); else File.WriteAllText(damaged, "corrupt");
        Assert.Equal(executable, await fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal(2, fixture.Downloads);
        Assert.Equal("launcher", File.ReadAllText(executable));
        Assert.Equal("dependency", File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "Prowl.Launcher.dll")));
        Assert.Null(fixture.Store.Settings.LauncherExecutable);
    }

    [Fact]
    public async Task FailedRepairRetainsTheExistingInstallationAndActiveVersion()
    {
        using Fixture fixture = new(_home);
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        File.Delete(Path.Combine(Path.GetDirectoryName(executable)!, "Prowl.Launcher.dll"));
        fixture.Store.Settings.LauncherExecutable = "working-version";
        fixture.Store.Save();
        fixture.CorruptDownload = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal("launcher", File.ReadAllText(executable));
        Assert.Equal("working-version", new LauncherStore(_home).Settings.LauncherExecutable);
    }

    [Fact]
    public async Task HealthyInstallationIsReusedWithoutADownload()
    {
        using Fixture fixture = new(_home);
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        Assert.Equal(executable, await fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal(1, fixture.Downloads);
    }

    [Fact]
    public async Task RetryRecoversAnInstallationMovedAsideBeforeACrash()
    {
        using Fixture fixture = new(_home);
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        string target = Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName;
        Directory.Move(target, target + ".previous");
        Assert.Equal(executable, await fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal(1, fixture.Downloads);
        Assert.False(Directory.Exists(target + ".previous"));
    }

    private sealed class Fixture : IDisposable
    {
        internal LauncherStore Store { get; }
        internal LauncherUpdaterService Updater { get; }
        internal EditorRelease Release { get; }
        internal int Downloads;
        internal bool CorruptDownload;
        private readonly HttpClient _http;

        internal Fixture(string home)
        {
            Store = new(home);
            using MemoryStream output = new();
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, true))
                foreach ((string name, string text) in new[] { ("Prowl.Launcher.exe", "launcher"), ("Prowl.Launcher.dll", "dependency") })
                {
                    using StreamWriter writer = new(archive.CreateEntry("App/" + name).Open());
                    writer.Write(text);
                }
            byte[] bytes = output.ToArray();
            Release = new(1, "v1.1.0", false, false, DateTimeOffset.UtcNow, "", null,
                [new("Prowl-Launcher-1.1.0-win-x64.zip", "https://github.com/dimmerly/Prowl-Launcher/releases/download/v1.1.0/update.zip",
                    bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))]);
            _http = new(new Handler(() =>
            {
                Downloads++;
                byte[] content = CorruptDownload ? bytes.Select(b => (byte)(b ^ 255)).ToArray() : bytes;
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            }));
            Updater = new(_http, Store);
        }
        public void Dispose() => _http.Dispose();
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond());
    }

    public void Dispose() { if (Directory.Exists(_home)) Directory.Delete(_home, true); }
}
