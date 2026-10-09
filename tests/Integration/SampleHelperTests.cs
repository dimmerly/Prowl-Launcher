using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class SampleHelperTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlSampleTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CatalogRejectsPathsAndDeduplicatesNames()
    {
        Assert.Equal(["HelloProwl", "NewSample"], SampleService.Discover(
            ["NewSample", "HelloProwl", "HelloProwl", "../Escape", "/Escape", "Other.dll", "Bad\n"])
            .Select(sample => sample.Id));
    }

    [Fact]
    public void LauncherEmbedsOnlyTheCatalogAndThumbnails()
    {
        string[] resources = typeof(Launcher).Assembly.GetManifestResourceNames();
        Assert.Contains("Prowl.Launcher.Samples.catalog.json", resources);
        Assert.DoesNotContain(resources, name => name.EndsWith(".zip"));
        Assert.NotEmpty(SampleService.Samples);
    }

    [Fact]
    public async Task DownloadContainsAllSamplesAndIsReusedAcrossInstancesOffline()
    {
        using BundleFixture fixture = new(_home);
        SampleService samples = new(fixture.Http, fixture.Store);
        string first = await samples.ExtractAsync(new("HelloProwl"), Path.Combine(_home, "first"));
        Assert.Equal("fixture assembly", File.ReadAllText(first));
        Assert.Contains(samples.Catalog, sample => sample.Id == "NewSample");
        Assert.Equal(1, fixture.Downloads);

        SampleService restarted = new(fixture.Http, new LauncherStore(_home));
        string second = await restarted.ExtractAsync(new("NewSample"), Path.Combine(_home, "second"), allowNetwork: false);
        Assert.Equal("fixture assembly", File.ReadAllText(second));
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Checks);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Store.WorkPath));

        var launch = SampleHelper.LaunchInfo(first);
        Assert.Equal(Path.GetDirectoryName(first), launch.WorkingDirectory);
        Assert.False(launch.UseShellExecute);
        Assert.True(launch.RedirectStandardOutput && launch.RedirectStandardError);
        Assert.Equal(["--run-sample", first], launch.ArgumentList);
    }

    [Fact]
    public async Task ConcurrentInstancesShareOneDownload()
    {
        using BundleFixture fixture = new(_home);
        SampleService first = new(fixture.Http, fixture.Store);
        SampleService second = new(fixture.Http, new LauncherStore(_home));
        await Task.WhenAll(first.EnsureDownloadedAsync(), second.EnsureDownloadedAsync());
        Assert.True(first.IsCached && second.IsCached);
        Assert.Equal(1, fixture.Downloads);
    }

    [Fact]
    public async Task BackgroundCheckDownloadsOnlyWhenTheUserChoosesToUpdate()
    {
        using BundleFixture fixture = new(_home);
        SampleService samples = new(fixture.Http, fixture.Store);
        await samples.EnsureDownloadedAsync();
        Assert.False(await samples.HasUpdateAsync());
        fixture.PublishUpdate();
        Assert.True(await samples.HasUpdateAsync());
        Assert.Equal(1, fixture.Downloads);
        await samples.EnsureDownloadedAsync(checkForUpdates: true);
        Assert.False(await samples.HasUpdateAsync());
        Assert.Equal(2, fixture.Downloads);
    }

    [Fact]
    public async Task FailedUpdateLeavesTheLocalBundleUsableOffline()
    {
        using BundleFixture fixture = new(_home);
        SampleService samples = new(fixture.Http, fixture.Store);
        await samples.EnsureDownloadedAsync();
        fixture.PublishUpdate("missing-host");
        await Assert.ThrowsAsync<InvalidDataException>(() => samples.EnsureDownloadedAsync(checkForUpdates: true));
        SampleService restarted = new(fixture.Http, new LauncherStore(_home));
        Assert.True(restarted.IsCached);
        string assembly = await restarted.ExtractAsync(new("HelloProwl"), Path.Combine(_home, "offline"), allowNetwork: false);
        Assert.Equal("fixture assembly", File.ReadAllText(assembly));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Store.WorkPath));
    }

    [Fact]
    public async Task CancelledDownloadCanBeRetriedWithoutPartialFiles()
    {
        using BundleFixture fixture = new(_home) { Stall = true };
        SampleService samples = new(fixture.Http, fixture.Store);
        using CancellationTokenSource cancellation = new();
        Task download = samples.EnsureDownloadedAsync(token: cancellation.Token);
        await fixture.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.False(samples.IsCached);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Store.WorkPath));

        fixture.Stall = false;
        await samples.EnsureDownloadedAsync();
        Assert.True(samples.IsCached);
        Assert.Equal(2, fixture.Downloads);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("traversal")]
    [InlineData("symlink")]
    [InlineData("missing-host")]
    [InlineData("missing-sample")]
    [InlineData("invalid-catalog")]
    public async Task InvalidBundleNeverActivatesOrLeavesStagingFiles(string failure)
    {
        using BundleFixture fixture = new(_home, failure);
        SampleService samples = new(fixture.Http, fixture.Store);
        await Assert.ThrowsAsync<InvalidDataException>(() => samples.EnsureDownloadedAsync());
        Assert.False(samples.IsCached);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Store.WorkPath));
        Assert.False(File.Exists(Path.Combine(fixture.Store.WorkPath, "escape.txt")));
    }

    [Fact]
    public async Task BundleFromAnotherRepositoryIsRejectedBeforeDownload()
    {
        using BundleFixture fixture = new(_home);
        fixture.Releases[0] = fixture.Releases[0] with
        {
            Assets = [fixture.Releases[0].Assets[0] with { DownloadUrl = "https://github.com/other/repo/releases/download/v1.0.0/samples.zip" }]
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new SampleService(fixture.Http, fixture.Store).EnsureDownloadedAsync());
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public async Task OfflineFirstUseExplainsThatADownloadIsRequired()
    {
        using BundleFixture fixture = new(_home);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SampleService(fixture.Http, fixture.Store).EnsureDownloadedAsync(allowNetwork: false));
        Assert.Equal(0, fixture.Checks);
    }

    [Fact]
    public async Task UnknownSampleAndCancelledExtractionCreateNoOutput()
    {
        using BundleFixture fixture = new(_home);
        SampleService samples = new(fixture.Http, fixture.Store);
        string output = Path.Combine(_home, "output");
        await Assert.ThrowsAsync<InvalidOperationException>(() => samples.ExtractAsync(new("../HelloProwl"), output));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => samples.ExtractAsync(new("HelloProwl"), output, cancellation.Token));
        Assert.False(Directory.Exists(output));
        Assert.Equal(0, fixture.Checks);
    }

    [Fact]
    public void SelectionUsesLatestAvailablePlatformBundleAndRespectsPrereleases()
    {
        using BundleFixture fixture = new(_home);
        EditorRelease stable = fixture.Releases[0];
        EditorRelease preview = stable with
        {
            Id = 2, Tag = "v2.0.0-preview.1", Preview = true, Published = stable.Published.AddDays(1),
            Assets = stable.Assets
        };
        EditorRelease missing = stable with { Id = 3, Published = stable.Published.AddDays(2), Assets = [] };
        Assert.Equal(stable.Id, SampleService.SelectBundle([preview, missing, stable], Platform.Identifier, false)!.Value.Release.Id);
        Assert.Equal(preview.Id, SampleService.SelectBundle([missing, stable, preview], Platform.Identifier, true)!.Value.Release.Id);
        Assert.Null(SampleService.SelectBundle([stable], "unsupported-platform", true));
        Assert.Null(SampleService.SelectBundle([stable with { Draft = true }], Platform.Identifier, true));
    }

    [SampleBundleFact]
    public async Task ReleaseBundleDownloadsAndExtractsEveryCompiledSample()
    {
        using BundleFixture fixture = new(_home, archive: Environment.GetEnvironmentVariable("PROWL_SAMPLE_BUNDLE"));
        SampleService samples = new(fixture.Http, fixture.Store);
        await samples.EnsureDownloadedAsync();
        foreach (Sample sample in samples.Catalog)
        {
            string assembly = await samples.ExtractAsync(sample, Path.Combine(_home, "runs", sample.Id));
            Assert.Equal(sample.Id, AssemblyName.GetAssemblyName(assembly).Name);
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(assembly)!, "*.cs", SearchOption.AllDirectories));
        }
        Assert.Equal(1, fixture.Downloads);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
    }

    private sealed class SampleBundleFactAttribute : FactAttribute
    {
        public SampleBundleFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("PROWL_SAMPLE_BUNDLE") == null)
                Skip = "Set PROWL_SAMPLE_BUNDLE to test the compiled release bundle.";
        }
    }

    private sealed class BundleFixture : IDisposable
    {
        private byte[] _archive;
        internal LauncherStore Store { get; }
        internal HttpClient Http { get; }
        internal List<EditorRelease> Releases { get; }
        internal int Checks;
        internal int Downloads;
        internal bool Stall;
        internal TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal BundleFixture(string home, string? failure = null, string? archive = null)
        {
            Store = new LauncherStore(home);
            _archive = archive == null ? CreateArchive(failure) : File.ReadAllBytes(archive);
            string digest = failure == "digest" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(_archive));
            string name = $"Prowl-Samples-{Platform.Identifier}.zip";
            Releases = [new(1, "v1.0.0", false, false, DateTimeOffset.UtcNow, "", null,
                [new(name, $"https://github.com/{Store.Settings.LauncherRepository}/releases/download/v1.0.0/{name}",
                    _archive.Length, "sha256:" + digest)])];
            Http = new HttpClient(new Handler(Respond));
        }

        private async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                Interlocked.Increment(ref Checks);
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Releases)) };
            }
            Interlocked.Increment(ref Downloads);
            DownloadStarted.TrySetResult();
            if (Stall) await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(_archive) };
        }

        internal void PublishUpdate(string? failure = null)
        {
            _archive = CreateArchive(failure, "second");
            EditorRelease previous = Releases[0];
            Releases[0] = previous with
            {
                Id = 2, Tag = "v2.0.0", Published = previous.Published.AddDays(1),
                Assets = [previous.Assets[0] with
                {
                    DownloadUrl = previous.Assets[0].DownloadUrl.Replace("/v1.0.0/", "/v2.0.0/"),
                    Size = _archive.Length, Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(_archive))
                }]
            };
        }

        private static byte[] CreateArchive(string? failure, string revision = "first")
        {
            using MemoryStream output = new();
            using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
            {
                void Add(string name, string text, int attributes = 0)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(name);
                    entry.ExternalAttributes = attributes;
                    using StreamWriter writer = new(entry.Open());
                    writer.Write(text);
                }
                Add("samples.json", failure == "invalid-catalog" ? "[\"../Escape\"]" : "[\"HelloProwl\",\"NewSample\"]");
                Add("revision.txt", revision);
                if (failure != "missing-host")
                    Add("Host/" + (OperatingSystem.IsWindows() ? "Prowl.SampleHost.exe" : "Prowl.SampleHost"), "fixture host");
                Add("Host/Prowl.SampleHost.dll", "fixture runtime");
                Add("Host/Prowl.SampleHost.runtimeconfig.json", "{}");
                Add("Samples/HelloProwl/HelloProwl.dll", "fixture assembly");
                if (failure != "missing-sample") Add("Samples/NewSample/NewSample.dll", "fixture assembly");
                Add("Samples/HelloProwl/Assets/keep.txt", "sample asset");
                if (failure == "traversal") Add("../escape.txt", "escape");
                if (failure == "symlink") Add("Host/link", "elsewhere", unchecked((int)0xa1ff0000));
            }
            return output.ToArray();
        }

        public void Dispose() => Http.Dispose();

        private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
        }
    }
}
