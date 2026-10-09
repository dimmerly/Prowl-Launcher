using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Prowl.Launcher;

using Xunit;

namespace Prowl.Launcher.Test;

public sealed class LauncherTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlLauncherTests", Guid.NewGuid().ToString("N"));
    private LauncherStore Store() => new( _home );

    [Fact]
    public void InstallationMetadata_UsesVersionAndPlatformDirectory()
    {
        LauncherStore store = Store();
        string directory = Path.Combine(store.VersionsPath, "v1.0-preview-4-win-x64");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "installation.json");
        File.WriteAllText(path, """
            {"ReleaseId":1,"Tag":"v1.0-preview-4","Runtime":"win-x64","ExecutableRelativePath":"Prowl.Editor.exe","InstalledAt":"2026-01-01T00:00:00Z"}
            """);

        InstalledEditor editor = Assert.Single(store.InstalledEditors());
        Assert.Equal("win-x64", editor.Platform);
        Assert.Equal("v1.0-preview-4-win-x64", editor.Key);
        Assert.Equal(directory, store.InstallPath(editor));
        LauncherStore.WriteJson(path, editor);
        Assert.Equal(editor, Assert.Single(new LauncherStore(_home).InstalledEditors()));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("/outside")]
    [InlineData("v1.0:stream")]
    [InlineData(".")]
    [InlineData("")]
    public void InstallationTagCannotEscapeVersionDirectory(string tag)
    {
        InstalledEditor editor = new(1, tag, "win-x64", "Prowl.Editor.exe", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidDataException>(() => Store().InstallPath(editor));
    }

    private static byte[] Archive(params (string path, string text)[] files)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new( stream, ZipArchiveMode.Create, true ))
        {
            foreach ((string path, string text) in files)
            {
                using StreamWriter writer = new( zip.CreateEntry(path).Open() );
                writer.Write(text);
            }
        }
        return stream.ToArray();
    }

    private static EditorRelease Release(byte[] archive, long id = 1, string? digest = null, string tag = "v1.0-preview-4") => new( id, tag, true, false,
        DateTimeOffset.UtcNow, $"https://github.com/ProwlEngine/Prowl/releases/tag/{tag}", "Notes",
        [
            new ReleaseAsset($"Prowl-{tag}-win-x64.zip", "https://github.com/ProwlEngine/Prowl/releases/download/test/editor.zip", archive.Length,
                digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(archive)))
        ] );

    private static byte[] EditorArchive(string text = "editor") => Archive(("Prowl/Prowl.Editor.exe", text),
        ("Prowl/Prowl.Editor.runtimeconfig.json", "{\"runtimeOptions\":{\"tfm\":\"net10.0\"}}"));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(request));
    }

    private static HttpClient Client(byte[] content) => new( new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content)
    }) );

    [Fact]
    public async Task Install_Repair_SideBySide_Uninstall_PreserveProjects()
    {
        LauncherStore store = Store();
        byte[] bytes = EditorArchive();
        using HttpClient http = Client(bytes);
        EditorInstallerService installer = new( http, store );
        InstalledEditor editor = await installer.InstallAsync(Release(bytes), "win-x64");
        Assert.Equal(editor.Key, store.Settings.DefaultEditorKey);
        Assert.Equal("editor", File.ReadAllText(store.ExecutablePath(editor)));
        Assert.Equal(10, installer.RequiredSdkMajor(editor));
        InstalledEditor other = await installer.InstallAsync(Release(bytes, 2, tag: "v1.0-preview-5"), "win-x64");
        Assert.Equal(2, store.InstalledEditors().Count);
        File.WriteAllText(store.ExecutablePath(editor), "corrupted");
        await installer.InstallAsync(Release(bytes), "win-x64");
        Assert.Equal("editor", File.ReadAllText(store.ExecutablePath(editor)));
        string project = Path.Combine(_home, "My project with spaces");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Project added = store.AddProject(project);
        added.EditorKey = editor.Key;
        store.Save();
        ProcessStartInfo launch = installer.LaunchInfo(editor, project);
        Assert.Equal(new[]
        {
            "--project",
            project
        }, launch.ArgumentList);
        installer.Uninstall(editor);
        Assert.Single(store.InstalledEditors());
        Assert.True(File.Exists(store.ExecutablePath(other)));
        Assert.True(Directory.Exists(project));
        Assert.Equal(editor.Key, new LauncherStore(_home).Settings.Projects.Single().EditorKey);
    }

    [Fact]
    public async Task BadDigest_DoesNotReplaceExistingEditor()
    {
        LauncherStore store = Store();
        byte[] good = EditorArchive();
        using HttpClient http = Client(good);
        EditorInstallerService installer = new( http, store );
        InstalledEditor editor = await installer.InstallAsync(Release(good), "win-x64");
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(good, digest: "sha256:" + new string('0', 64)), "win-x64"));
        Assert.Equal("editor", File.ReadAllText(store.ExecutablePath(editor)));
        Assert.Empty(Directory.EnumerateDirectories(store.WorkPath));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Prowl/../../outside.txt")]
    [InlineData("Prowl\\..\\..\\outside.txt")]
    [InlineData("/outside.txt")]
    public void ArchiveCannotEscapeStaging(string name)
    {
        Directory.CreateDirectory(_home);
        string archive = Path.Combine(_home, "attack.zip");
        File.WriteAllBytes(archive, Archive((name, "bad")));
        Assert.Throws<InvalidDataException>(() => EditorInstallerService.ExtractArchive(archive, Path.Combine(_home, "stage")));
        Assert.False(File.Exists(Path.Combine(_home, "outside.txt")));
    }

    [Fact]
    public void ArchiveLinksAreRejected()
    {
        Directory.CreateDirectory(_home);
        string archive = Path.Combine(_home, "link.zip");
        using (FileStream stream = File.Create(archive))
        using (ZipArchive zip = new( stream, ZipArchiveMode.Create ))
        {
            ZipArchiveEntry link = zip.CreateEntry("Prowl/link");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        Assert.Throws<InvalidDataException>(() => EditorInstallerService.ExtractArchive(archive, Path.Combine(_home, "stage")));
    }

    [Fact]
    public async Task CancelledInstallation_DoesNotRegisterAnEditor()
    {
        byte[] bytes = EditorArchive();
        using HttpClient http = Client(bytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        LauncherStore store = Store();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EditorInstallerService(http, store).InstallAsync(Release(bytes), "win-x64", token: cancellation.Token));
        Assert.Empty(store.InstalledEditors());
        Assert.Empty(Directory.EnumerateDirectories(store.WorkPath));
    }

    [Fact]
    public async Task CatalogIncludesPreviews_AndFallsBackToCacheOffline()
    {
        byte[] bytes = EditorArchive();
        EditorRelease release = Release(bytes);
        using HttpClient http = Client(JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            release
        }));
        LauncherStore store = Store();
        GitHubReleasesService github = new( http, store );
        Assert.True((await github.GetAsync()).Single().Preview);
        using HttpClient offline = new( new Handler(_ => throw new HttpRequestException("Offline")) );
        GitHubReleasesService cached = new( offline, store );
        Assert.Equal(release.Tag, (await cached.GetAsync()).Single().Tag);
        Assert.True(cached.UsedCache);
    }

    [Fact]
    public void ProjectVersionPinSurvivesReload_AndDoesNotChangeProjectFiles()
    {
        LauncherStore store = Store();
        string project = Path.Combine(_home, "Project");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        string marker = Path.Combine(project, "Project.prowl");
        const string original = "{\"name\":\"My game\",\"version\":\"1.0-preview.4\"}";
        File.WriteAllText(marker, original);
        InstalledEditor editor = new( 1, "v1.0-preview-4", "win-x64", "Prowl/Prowl.Editor.exe", DateTimeOffset.UtcNow );
        LauncherStore.WriteJson(Path.Combine(store.InstallPath(editor), "installation.json"), editor);
        Project added = store.AddProject(marker);
        Assert.Equal("My game", added.Name);
        Assert.Equal(editor.Key, added.EditorKey);
        Assert.Same(added, store.AddProject(project));
        Assert.Equal(editor.Key, new LauncherStore(_home).Settings.Projects.Single().EditorKey);
        Assert.Equal(original, File.ReadAllText(marker));
    }

    [Fact]
    public void RemovedImportedProject_StaysRemovedAfterRestart()
    {
        LauncherStore store = Store();
        string project = Path.Combine(_home, "Project");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        string recent = Path.Combine(_home, "recent.json");
        LauncherStore.WriteJson(recent, new[]
        {
            new Project
            {
                Path = project
            },
            new Project
            {
                Path = Path.Combine(_home, "Missing")
            }
        });
        store.ImportRecentProjects(recent);
        Assert.Single(store.Settings.Projects);
        store.Settings.Projects.Clear();
        store.Save();
        LauncherStore reopened = new( _home );
        reopened.ImportRecentProjects(recent);
        Assert.Empty(reopened.Settings.Projects);
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("1.0.0", "1.0.0+build", false)]
    [InlineData("1.0.0", "1.0.0-preview.1", true)]
    [InlineData("1.0.0-preview.1", "1.0.0", false)]
    [InlineData("1.0.0-preview.10", "1.0.0-preview.9", true)]
    [InlineData("1.0.0-preview.9", "1.0.0-preview.10", false)]
    [InlineData("1.0.0-preview.1", "1.0.0-preview.1", false)]
    [InlineData("v1.1.0", "1.0.0", false)]
    public void LauncherUpdates_OnlyOfferNewerVersions(string tag, string current, bool expected)
        => Assert.Equal(expected, LauncherUpdaterService.IsNewer(tag, current));

    [Fact]
    public void LauncherUpdates_CompareAssetVersionsAcrossEditorReleases()
    {
        EditorRelease WithLauncher(long id, string editorVersion, string launcherVersion) => Release([], id) with
        {
            Tag = editorVersion,
            Preview = false,
            Assets = [new ReleaseAsset($"Prowl Launcher-{launcherVersion}-win-x64.zip", "https://example.com/launcher.zip", 1, null)]
        };

        EditorRelease initial = WithLauncher(1, "v2.0.0", "1.0.0");
        EditorRelease editorOnly = WithLauncher(2, "v3.0.0", "1.0.0");
        Assert.Null(LauncherUpdaterService.FindUpdate([editorOnly, initial], "win-x64", "1.0.0"));

        EditorRelease newer = WithLauncher(3, "v4.0.0", "1.2.0");
        EditorRelease republishedOlder = WithLauncher(4, "v5.0.0", "1.1.0");
        EditorRelease preview = WithLauncher(5, "v6.0.0-preview.1", "2.0.0") with { Preview = true };
        EditorRelease draft = WithLauncher(6, "v7.0.0", "3.0.0") with { Draft = true };
        EditorRelease[] releases = [draft, preview, republishedOlder, newer, editorOnly, initial];
        Assert.Equal(newer, LauncherUpdaterService.FindUpdate(releases, "win-x64", "1.0.0"));
        Assert.Null(LauncherUpdaterService.FindUpdate(releases, "linux-x64", "1.0.0"));
        Assert.Null(LauncherUpdaterService.FindUpdate(releases, "win-x64", "1.2.0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LauncherUpdates_PrereleasesRequireOptIn(bool includePrereleases)
    {
        EditorRelease stable = Release([]) with
        {
            Preview = false,
            Assets = [new ReleaseAsset("Prowl Launcher-1.1.0-win-x64.zip", "", 0, null)]
        };
        EditorRelease preview = stable with
        {
            Id = 2,
            Preview = true,
            Assets = [new ReleaseAsset("Prowl Launcher-1.2.0-preview-1-win-x64.zip", "", 0, null)]
        };
        EditorRelease draft = preview with { Id = 3, Draft = true };
        Assert.Equal(includePrereleases ? preview : stable,
            LauncherUpdaterService.FindUpdate([draft, preview, stable], "win-x64", "1.0.0", includePrereleases));
        Assert.Null(LauncherUpdaterService.FindUpdate([preview], "win-x64", "1.2.0", includePrereleases));
        Assert.Null(LauncherUpdaterService.FindUpdate([preview with { Preview = false }], "win-x64", "1.0.0"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LauncherUpdate_PrereleaseInstallationHonorsSavedOptIn(bool optIn, bool previewFlag)
    {
        byte[] bytes = Archive(("Prowl Launcher/Prowl.Launcher.exe", "preview"));
        LauncherStore store = Store();
        Assert.False(store.Settings.LauncherPrereleases);
        store.Settings.LauncherPrereleases = optIn;
        store.Save();
        EditorRelease release = Release(bytes) with
        {
            Preview = previewFlag,
            Assets = [new ReleaseAsset("Prowl Launcher-1.1.0-preview-1-win-x64.zip",
                "https://github.com/dimmerly/Prowl-Launcher/releases/download/v1.1.0-preview-1/launcher.zip",
                bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))]
        };
        using HttpClient http = Client(bytes);
        LauncherUpdaterService updater = new(http, new LauncherStore(_home));
        if (optIn)
            Assert.Equal("preview", File.ReadAllText(await updater.InstallAsync(release, "win-x64")));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => updater.InstallAsync(release, "win-x64"));
    }

    [Fact]
    public void LauncherUpdates_OptingOutOffersLatestStableEvenWhenOlder()
    {
        EditorRelease Stable(long id, string version) => Release([], id) with
        {
            Preview = false,
            Assets = [new ReleaseAsset($"Prowl Launcher-{version}-win-x64.zip", "", 0, null)]
        };
        EditorRelease older = Stable(1, "1.0.0");
        EditorRelease latest = Stable(2, "1.1.0");
        EditorRelease preview = Stable(3, "1.2.0-preview-2") with { Preview = true };
        EditorRelease draft = Stable(4, "2.0.0") with { Draft = true };
        EditorRelease[] releases = [draft, preview, older, latest];
        Assert.Equal(latest, LauncherUpdaterService.FindUpdate(releases, "win-x64", "1.2.0-preview-1"));
        Assert.Equal(preview, LauncherUpdaterService.FindUpdate(releases, "win-x64", "1.2.0-preview-1", true));
        Assert.Null(LauncherUpdaterService.FindUpdate([preview], "win-x64", "1.2.0-preview-1"));
        Assert.Null(LauncherUpdaterService.FindUpdate(releases, "win-x64", "1.2.0"));
    }

    [Fact]
    public async Task LauncherUpdate_ConfiguredRepositoryHasSeparateInstallations()
    {
        byte[] bytes = Archive(("Prowl Launcher/Prowl.Launcher.exe", "launcher"));
        LauncherStore store = Store();
        EditorRelease release = Release(bytes) with
        {
            Preview = false,
            Assets = [new ReleaseAsset("Prowl Launcher-1.1.0-win-x64.zip",
                "https://github.com/dimmerly/Prowl-Launcher/releases/download/v1.1.0/launcher.zip",
                bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))]
        };
        using HttpClient http = Client(bytes);
        LauncherUpdaterService updater = new(http, store);
        string official = await updater.InstallAsync(release, "win-x64");
        store.Settings.LauncherRepository = "someone/custom-launcher";
        store.Save();
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.InstallAsync(release, "win-x64"));
        ReleaseAsset asset = release.Assets.Single() with
        {
            DownloadUrl = "https://github.com/someone/custom-launcher/releases/download/v1.1.0/launcher.zip"
        };
        string custom = await updater.InstallAsync(release with { Assets = [asset] }, "win-x64");
        Assert.NotEqual(official, custom);
        Assert.True(File.Exists(official));
        Assert.Equal("launcher", File.ReadAllText(custom));
        Assert.Equal(store.Settings.LauncherRepository, new LauncherStore(_home).Settings.LauncherRepository);
    }

    [Theory]
    [InlineData("Prowl Launcher-1.2.3-win-x64.zip", "win-x64", "1.2.3")]
    [InlineData("Prowl-Launcher-1.2.3-win-x64.zip", "win-x64", "1.2.3")]
    [InlineData("Prowl.Launcher-1.2.3-win-x64.zip", "win-x64", "1.2.3")]
    [InlineData("Prowl.Launcher-1.2.3-preview-1-win-x64.zip", "win-x64", "1.2.3-preview-1")]
    [InlineData("Prowl Launcher-1.2.3-preview.1+build-win-x64.zip", "win-x64", "1.2.3-preview.1+build")]
    [InlineData("Prowl Launcher-1.2.3-win-x64.zip", "linux-x64", null)]
    [InlineData("Prowl-v9.0.0-win-x64.zip", "win-x64", null)]
    [InlineData("Prowl Launcher-not-a-version-win-x64.zip", "win-x64", null)]
    public void LauncherAssetVersion_IsIndependentOfEditorTag(string name, string platform, string? expected)
        => Assert.Equal(expected, LauncherUpdaterService.VersionFor(new ReleaseAsset(name, "", 0, null), platform));

    [Fact]
    public async Task LauncherUpdate_IsInstalledSideBySide_AndPersistsForwardingPath()
    {
        byte[] bytes = Archive(("Prowl Launcher/Prowl.Launcher.exe", "launcher"));
        EditorRelease release = Release(bytes) with
        {
            Tag = "v9.0.0",
            Preview = false,
            Assets =
            [
                new ReleaseAsset("Prowl Launcher-1.1.0-win-x64.zip", "https://github.com/dimmerly/Prowl-Launcher/releases/download/test/launcher.zip",
                    bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))
            ]
        };
        LauncherStore store = Store();
        using HttpClient http = Client(bytes);
        LauncherUpdaterService updater = new( http, store );
        string executable = await updater.InstallAsync(release, "win-x64");
        Assert.Equal("launcher", File.ReadAllText(executable));
        Assert.Equal(executable, new LauncherStore(_home).Settings.LauncherExecutable);
        Assert.Equal(executable, await updater.InstallAsync(release, "win-x64"));
        Assert.Equal(executable, await updater.InstallAsync(release with { Id = 99, Tag = "v10.0.0" }, "win-x64"));
    }

    [Fact]
    public async Task LauncherUpdates_RejectAssetsFromAnotherRepositoryBeforeDownloading()
    {
        EditorRelease release = Release([]) with
        {
            Preview = false,
            Assets = [new ReleaseAsset("Prowl Launcher-1.1.0-win-x64.zip",
                "https://github.com/someone/another-repo/releases/download/v1.1.0/launcher.zip", 1, null)]
        };
        using HttpClient http = new(new Handler(_ => throw new Exception("Should not download an untrusted update.")));
        LauncherUpdaterService updater = new(http, Store());
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.InstallAsync(release, "win-x64"));
    }

    [Fact]
    public async Task LauncherReleaseLookup_DoesNotUseConfiguredEditorRepository()
    {
        LauncherStore store = Store();
        store.Settings.ProwlRepository = "someone/custom-editor";
        store.Settings.LauncherRepository = "someone/custom-launcher";
        Uri? requested = null;
        using HttpClient http = new(new Handler(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        GitHubReleasesService github = new(http, store, store.Settings.LauncherRepository);
        await github.GetAsync();
        Assert.Equal($"/repos/{store.Settings.LauncherRepository}/releases", requested!.AbsolutePath);
    }

    [LiveFact]
    [Trait("Category", "Live")]
    public async Task LiveGitHubRelease_DownloadsVerifiesAndInstallsCurrentPlatform()
    {
        LauncherStore store = Store();
        using HttpClient http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        GitHubReleasesService github = new( http, store );
        EditorRelease release = (await github.GetAsync()).First(r => r.AssetFor(Platform.Identifier) != null);
        EditorInstallerService installer = new( http, store );
        InstalledEditor editor = await installer.InstallAsync(release, Platform.Identifier);
        Assert.True(new FileInfo(store.ExecutablePath(editor)).Length > 0);
        Assert.Single(store.InstalledEditors());
        Assert.True(installer.RequiredSdkMajor(editor) >= 8);
        installer.Uninstall(editor);
        Assert.Empty(store.InstalledEditors());
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROWL_LAUNCHER_LIVE_TEST") != "1")
        {
            Skip = "Set PROWL_LAUNCHER_LIVE_TEST=1 to download an actual GitHub release.";
        }
    }
}
