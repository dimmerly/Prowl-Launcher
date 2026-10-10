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
        using Fixture fixture = new( _home );
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        string damaged = Path.Combine(file == "launcher-installation.json"
            ? Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName : Path.GetDirectoryName(executable)!, file);
        if (delete)
        {
            File.Delete(damaged);
        }
        else
        {
            File.WriteAllText(damaged, "corrupt");
        }
        Assert.Equal(executable, await fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal(2, fixture.Downloads);
        Assert.Equal("launcher", File.ReadAllText(executable));
        Assert.Equal("dependency", File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "Prowl.Launcher.dll")));
        Assert.Null(fixture.Store.Settings.LauncherExecutable);
    }

    [Fact]
    public async Task FailedRepairRetainsTheExistingInstallationAndActiveVersion()
    {
        using Fixture fixture = new( _home );
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
        using Fixture fixture = new( _home );
        string executable = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        Assert.Equal(executable, await fixture.Updater.InstallAsync(fixture.Release, "win-x64"));
        Assert.Equal(1, fixture.Downloads);
    }

    [Fact]
    public async Task CleanupRemovesUnusedUpdatesAndPreservesTheActiveAndInstalledCopies()
    {
        using Fixture fixture = new( _home );
        async Task<string> Install(string version) => await fixture.Updater.InstallAsync(fixture.Release with
        {
            Assets =
            [
                fixture.Release.Assets[0] with
                {
                    Name = $"Prowl-Launcher-{version}-win-x64.zip"
                }
            ]
        }, "win-x64");
        string old = await Install("1.0.0");
        string original = await Install("1.1.0");
        string active = await Install("1.2.0");
        fixture.Store.Settings.LauncherExecutable = active;
        fixture.Store.Settings.InstalledLauncherExecutable = original;
        fixture.Store.Save();

        LauncherUpdaterService.Cleanup(fixture.Store);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(active));
        Assert.Equal(2, Directory.EnumerateDirectories(Path.Combine(LauncherUpdaterService.UpdatesPath(fixture.Store),
            GitHubRepositoryHelper.CacheKey(fixture.Store.Settings.LauncherRepository))).Count());
    }

    [Fact]
    public async Task CleanupReadsTheSavedActiveVersionAndRemovesUnusedLegacyDirectories()
    {
        using Fixture fixture = new( _home );
        string active = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        LauncherStore other = new( _home );
        other.Settings.LauncherExecutable = active;
        other.Save();
        string legacy = Path.Combine(_home, "LauncherVersions", "repository", "old-version");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Prowl.Launcher.exe"), "old");

        LauncherUpdaterService.Cleanup(fixture.Store);

        Assert.True(File.Exists(active));
        Assert.False(Directory.Exists(Path.Combine(_home, "LauncherVersions")));
    }

    [Fact]
    public async Task CleanupCannotRemoveAnUpdateWhileItsActivationIsInProgress()
    {
        using Fixture fixture = new( _home );
        string pending = await fixture.Updater.InstallAsync(fixture.Release, "win-x64");
        using FileStream update = LauncherUpdaterService.LockUpdates(fixture.Store);
        Assert.Throws<IOException>(() => LauncherUpdaterService.Cleanup(fixture.Store));
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public void CleanupRejectsLinksAndCannotDeleteAnOutsideDirectory()
    {
        using Fixture fixture = new( _home );
        string outside = Path.Combine(_home, "UnrelatedData");
        Directory.CreateDirectory(outside);
        string keep = Path.Combine(outside, "keep.txt");
        File.WriteAllText(keep, "keep");
        string updates = LauncherUpdaterService.UpdatesPath(fixture.Store);
        Directory.CreateDirectory(updates);
        string linked = Path.Combine(updates, "repository");
        try
        {
            Directory.CreateSymbolicLink(linked, outside);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            return; // Creating symlinks requires Developer Mode or elevation on Windows.
        }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            return; // ERROR_PRIVILEGE_NOT_HELD on Windows without Developer Mode.
        }
        try
        {
            Assert.Throws<IOException>(() => LauncherUpdaterService.Cleanup(fixture.Store));
            Assert.Equal("keep", File.ReadAllText(keep));
        }
        finally
        {
            Directory.Delete(linked);
        }
    }

    [Fact]
    public async Task RetryRecoversAnInstallationMovedAsideBeforeACrash()
    {
        using Fixture fixture = new( _home );
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
            Store = new LauncherStore(home);
            using MemoryStream output = new();
            using (ZipArchive archive = new( output, ZipArchiveMode.Create, true ))
            {
                foreach ((string name, string text) in new[]
                    {
                        ("Prowl.Launcher.exe", "launcher"),
                        ("Prowl.Launcher.dll", "dependency")
                    })
                {
                    using StreamWriter writer = new( archive.CreateEntry("App/" + name).Open() );
                    writer.Write(text);
                }
            }
            byte[] bytes = output.ToArray();
            Release = new EditorRelease(1, "v1.1.0", false, false, DateTimeOffset.UtcNow, "", null,
            [
                new ReleaseAsset("Prowl-Launcher-1.1.0-win-x64.zip", "https://github.com/dimmerly/Prowl-Launcher/releases/download/v1.1.0/update.zip",
                    bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))
            ]);
            _http = new HttpClient(new Handler(() =>
            {
                Downloads++;
                byte[] content = CorruptDownload ? bytes.Select(b => (byte)(b ^ 255)).ToArray() : bytes;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                };
            }));
            Updater = new LauncherUpdaterService(_http, Store);
        }
        public void Dispose() => _http.Dispose();
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond());
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}
