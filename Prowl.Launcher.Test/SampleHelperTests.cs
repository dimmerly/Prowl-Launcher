using System.Diagnostics;
using System.Reflection;
using Prowl.Launcher;
using Xunit;

namespace Prowl.Launcher.Test;

public sealed class SampleHelperTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlSampleTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiscoveryIncludesNewBundlesWithoutCatalogEntriesOrThumbnails()
    {
        IReadOnlyList<Sample> samples = SampleService.Discover([
            "Prowl.Launcher.Samples.NewSample.zip",
            "Prowl.Launcher.Samples.HelloProwl.zip",
            "Prowl.Launcher.Samples.HelloProwl.png",
            "Other.Samples.NotASample.zip",
            "Prowl.Launcher.Samples../Escape.zip"
        ]);
        Assert.Equal(new[] { "HelloProwl", "NewSample" }, samples.Select(sample => sample.Id));
    }

    public static IEnumerable<object[]> Samples() => SampleService.Samples.Select(sample => new object[] { sample.Id });

    [BundledSamplesTheory]
    [MemberData(nameof(Samples))]
    public async Task EverySampleIsEmbeddedAndExtractsWithoutSourceFilesOrCompiler(string id)
    {
        Sample sample = new(id);
        Assert.True(SampleService.IsAvailable(sample));
        string output = Path.Combine(_home, sample.Id);
        string assembly = await SampleService.ExtractAsync(sample, output);
        Assert.Equal(sample.Id, AssemblyName.GetAssemblyName(assembly).Name);
        Assert.Empty(Directory.EnumerateFiles(output, "*.cs", SearchOption.AllDirectories));
        if (sample.Id == "RenderingShowcase")
            Assert.True(File.Exists(Path.Combine(output, "Assets", "Textures", "Bricks.png")));
        ProcessStartInfo launch = SampleHelper.LaunchInfo(assembly);
        Assert.Equal(output, launch.WorkingDirectory);
        Assert.False(launch.UseShellExecute);
        Assert.True(launch.CreateNoWindow);
        Assert.True(launch.RedirectStandardOutput);
        Assert.True(launch.RedirectStandardError);
        Assert.Equal(new[] { "--run-sample", assembly }, launch.ArgumentList.TakeLast(2));
    }

    [Fact]
    public async Task UnknownSampleCannotEscapeCatalog()
    {
        Sample sample = new("../HelloProwl");
        Assert.False(SampleService.IsAvailable(sample));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SampleService.ExtractAsync(sample, _home));
        Assert.False(Directory.Exists(_home));
    }

    [Fact]
    public async Task CancelledExtractionDoesNotCreateOutput()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SampleService.ExtractAsync(new Sample("HelloProwl"), _home, cancelled.Token));
        Assert.False(Directory.Exists(_home));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    private sealed class BundledSamplesTheoryAttribute : TheoryAttribute
    {
        public BundledSamplesTheoryAttribute()
        {
            if (SampleService.Samples.Count == 0)
                Skip = "Build sample bundles and set SampleBundleDirectory to test sample extraction.";
        }
    }
}
