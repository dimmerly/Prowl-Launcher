using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Prowl.Launcher.Test.E2E;

internal sealed class LauncherFixture : IDisposable
{
    internal string Root { get; }
    internal string Home => Path.Combine(Root, "home");
    internal string Projects => Path.Combine(Root, "projects");
    internal LauncherStore Store { get; }
    internal HttpClient Http { get; }
    internal EditorRelease EditorRelease { get; }
    internal EditorRelease StableUpdate { get; }
    internal EditorRelease PreviewUpdate { get; }
    internal int LauncherChecks;
    internal int Downloads;
    internal bool SlowDownload;
    internal bool CorruptDownload;
    internal bool DelaySampleDownload;
    private byte[]? _sampleZip;
    private EditorRelease? _sampleRelease;
    internal int SampleDownloads;
    private readonly byte[] _editorZip;
    private readonly byte[] _launcherZip;

    internal LauncherFixture(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Projects);
        bool fresh = !File.Exists(Path.Combine(Home, "settings.json"));
        Store = new LauncherStore(Home);
        if (fresh)
        {
            Store.Settings.Locale = "en";
            Store.Settings.RecentProjectsImported = true;
            Store.Settings.RecentProjectMetadataImported = true;
            Store.Settings.InstallationPromptHandled = true;
            Store.Save();
        }
        _editorZip = ProbeArchive("Prowl.Editor");
        _launcherZip = ProbeArchive("Prowl.Launcher");
        string platform = Platform.Identifier;
        EditorRelease = Release(11, "v1.0.0", false, "ProwlEngine/Prowl", $"Prowl-v1.0.0-{platform}.zip", _editorZip);
        StableUpdate = Release(201, "v2.0.0", false, "dimmerly/Prowl-Launcher", $"Prowl-Launcher-2.0.0-{platform}.zip", _launcherZip);
        PreviewUpdate = Release(202, "v3.0.0-preview.1", true, "dimmerly/Prowl-Launcher", $"Prowl-Launcher-3.0.0-preview.1-{platform}.zip", _launcherZip);
        Http = new HttpClient(new Handler(Respond)) { Timeout = TimeSpan.FromSeconds(20) };
    }

    internal async Task<InstalledEditor> InstallEditor()
    {
        var installed = await new EditorInstallerService(Http, Store).InstallAsync(EditorRelease, Platform.Identifier);
        return installed;
    }

    internal Project AddProject(string name, InstalledEditor? editor = null)
    {
        string path = Path.Combine(Projects, name);
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        LauncherStore.WriteJson(Path.Combine(path, name + ".prowl"), new { name, version = "1.0.0" });
        File.WriteAllText(Path.Combine(path, "Assets", "keep.txt"), "User project data");
        Project project = Store.AddProject(path);
        if (editor != null) { project.EditorKey = editor.Key; Store.Save(); }
        return project;
    }

    internal Settings Saved() => new LauncherStore(Home).Settings;
    internal string LaunchRecord => Path.Combine(Home, "editor-launch.json");

    internal void PublishSamples(bool newer = false)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
        {
            using MemoryStream hostBytes = new(ProbeArchive("Prowl.SampleHost"));
            using ZipArchive host = new(hostBytes, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in host.Entries)
            {
                using Stream source = entry.Open();
                using Stream target = zip.CreateEntry(entry.FullName.Replace("Probe/", "Host/")).Open();
                source.CopyTo(target);
            }
            void Add(string path, string text)
            {
                using StreamWriter writer = new(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
            Add("Host/Prowl.SampleHost.dll", "probe");
            Add("Host/Prowl.SampleHost.runtimeconfig.json", "{}");
            Add("samples.json", "[\"HelloProwl\",\"PhysicsShowcase\"]");
            Add("Samples/HelloProwl/HelloProwl.dll", newer ? "new sample" : "sample");
            Add("Samples/PhysicsShowcase/PhysicsShowcase.dll", "sample");
        }
        _sampleZip = output.ToArray();
        _sampleRelease = Release(newer ? 302 : 301, "v2.0.0", false, Store.Settings.LauncherRepository,
            $"Prowl-Samples-{Platform.Identifier}.zip", _sampleZip);
    }

    private Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken token)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "api.github.com")
        {
            if (path.EndsWith("/Prowl/releases", StringComparison.Ordinal)) return Json(new[] { EditorRelease });
            Interlocked.Increment(ref LauncherChecks);
            return Json(_sampleRelease == null ? new[] { PreviewUpdate, StableUpdate } : new[] { _sampleRelease, PreviewUpdate, StableUpdate });
        }
        Interlocked.Increment(ref Downloads);
        byte[] archive = path.Contains("/ProwlEngine/Prowl/", StringComparison.Ordinal) ? _editorZip : _launcherZip;
        if (path.Contains("Prowl-Samples-", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref SampleDownloads);
            archive = _sampleZip!;
            if (DelaySampleDownload) return DelayedSampleResponse(archive, token);
        }
        if (SlowDownload) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) });
        if (CorruptDownload) archive = archive.Select((b, i) => i == 20 ? (byte)(b ^ 255) : b).ToArray();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
    }

    private static async Task<HttpResponseMessage> DelayedSampleResponse(byte[] archive, CancellationToken token)
    {
        await Task.Delay(1500, token);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
    }

    private static Task<HttpResponseMessage> Json<T>(T value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value)) });

    private static EditorRelease Release(long id, string tag, bool preview, string repository, string name, byte[] bytes) =>
        new(id, tag, preview, false, DateTimeOffset.UtcNow, $"https://github.com/{repository}/releases/tag/{tag}",
            "## Fixture release notes\n\n- **New feature:** reliable workflows\n- Fixes and improvements\n\n[Full details](https://github.com/" + repository + ")",
            [new ReleaseAsset(name, $"https://github.com/{repository}/releases/download/{tag}/{name}", bytes.Length,
                "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)))]);

    private static byte[] ProbeArchive(string name)
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, true))
        {
            // The same test executable has a tiny probe entry point; no extra project is needed.
            string executable = "Prowl.Launcher.Test" + (OperatingSystem.IsWindows() ? ".exe" : "");
            string probe = Path.Combine(AppContext.BaseDirectory, executable);
            if (!File.Exists(probe)) throw new FileNotFoundException("Build the test executable before running E2E tests.", probe);
            string? graphics = Environment.GetEnvironmentVariable("PROWL_SMOKE_OPENGL_DIRECTORY");
            HashSet<string> graphicsLibraries = graphics == null ? [] : Directory.GetFiles(graphics, "*.dll")
                .Select(file => Path.GetFileName(file)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(AppContext.BaseDirectory, file).Replace('\\', '/');
                if (relative.StartsWith("e2e/", StringComparison.Ordinal) || relative.StartsWith("TestResults/", StringComparison.Ordinal)) continue;
                if (graphicsLibraries.Contains(Path.GetFileName(file))) continue;
                string extension = Path.GetExtension(file);
                if (file != probe && extension is not (".dll" or ".json" or ".so" or ".dylib")) continue;
                string entryName = file == probe ? name + (OperatingSystem.IsWindows() ? ".exe" : "") : relative;
                ZipArchiveEntry entry = archive.CreateEntry("Probe/" + entryName, CompressionLevel.Fastest);
                entry.ExternalAttributes = unchecked((int)(file == probe ? 0x81ed0000 : 0x81a40000));
                using Stream target = entry.Open();
                using Stream source = File.OpenRead(file);
                source.CopyTo(target);
            }
            if (name == "Prowl.Editor")
            {
                using StreamWriter config = new(archive.CreateEntry("Probe/Prowl.Editor.runtimeconfig.json").Open());
                config.Write("{\"runtimeOptions\":{\"tfm\":\"net10.0\"}}");
            }
        }
        return output.ToArray();
    }

    internal static void InstallSoftwareGraphics()
    {
        string? graphics = Environment.GetEnvironmentVariable("PROWL_SMOKE_OPENGL_DIRECTORY");
        if (graphics == null) return;
        foreach (string library in Directory.GetFiles(graphics, "*.dll"))
            File.Copy(library, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(library)), true);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose() => Http.Dispose();
}
