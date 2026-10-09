using Xunit;

namespace Prowl.Launcher.Test;

public sealed class EditorUpdateIndicatorTests
{
    private const string Repository = GitHubRepositoryHelper.DefaultProwl;
    private const string Platform = "win-x64";
    private static InstalledEditor Installed(string tag, string repository = Repository, string platform = Platform) =>
        new( 1, tag, platform, "Prowl.Editor.exe", DateTimeOffset.UtcNow )
        {
            Repository = repository
        };
    private static EditorRelease Release(string tag, bool preview = false, bool draft = false, string platform = Platform) =>
        new( 2, tag, preview, draft, DateTimeOffset.UtcNow, "", null, [new ReleaseAsset($"Prowl-{tag}-{platform}.zip", "", 1, null)] );

    [Theory]
    [InlineData("v1.1.0", false, false, true)]
    [InlineData("v1.1.0-preview.1", true, false, false)]
    [InlineData("v1.1.0-preview.1", true, true, true)]
    [InlineData("v1.1.0-preview.1", false, false, false)]
    [InlineData("v1.0.0", false, true, false)]
    [InlineData("v0.9.0", false, true, false)]
    public void IndicatorRespectsTheSelectedChannelAndVersionOrder(string tag, bool preview, bool includePreviews, bool expected)
    {
        EditorRelease? update = Launcher.FindEditorUpdate([Release(tag, preview)], [Installed("v1.0.0")], Repository, Platform, includePreviews);
        Assert.Equal(expected, update != null);
    }

    [Theory]
    [InlineData("v1.0-preview-4", "v1.0-preview-5")]
    [InlineData("v1.0.0-preview.9", "v1.0.0-preview.10")]
    [InlineData("v1.0.0-preview.10", "v1.0.0")]
    public void HistoricalTagsAndPrereleaseNumbersCompareCorrectly(string installed, string available) => Assert.NotNull(Launcher.FindEditorUpdate([Release(available, available.Contains('-'))], [Installed(installed)], Repository, Platform, true));

    [Fact]
    public void OnlyTheNewestInstalledVersionIsTheBaseline()
    {
        Assert.Null(Launcher.FindEditorUpdate([Release("v1.1.0")], [Installed("v1.0.0"), Installed("v1.2.0")], Repository, Platform, true));
        Assert.Null(Launcher.FindEditorUpdate([Release("v1.1.0")], [Installed("v1.2.0-preview.1")], Repository, Platform, false));
    }

    [Fact]
    public void TheNewestEligibleReleaseIsSelectedRegardlessOfPublicationOrder()
    {
        EditorRelease update = Release("v1.2.0");
        Assert.Same(update, Launcher.FindEditorUpdate([update, Release("v1.1.0")], [Installed("v1.0.0")], Repository, Platform, false));
    }

    [Fact]
    public void InstallingTheReleaseClearsTheIndicator()
    {
        EditorRelease release = Release("v1.1.0");
        Assert.NotNull(Launcher.FindEditorUpdate([release], [Installed("v1.0.0")], Repository, Platform, false));
        Assert.Null(Launcher.FindEditorUpdate([release], [Installed("v1.0.0"), Installed("v1.1.0")], Repository, Platform, false));
    }

    [Fact]
    public void DraftsUnsupportedPlatformsAndInvalidVersionsDoNotShowAnIndicator() => Assert.Null(Launcher.FindEditorUpdate([Release("v2.0.0", draft: true), Release("v2.0.0", platform: "linux-x64"), Release("unknown")],
        [Installed("v1.0.0")], Repository, Platform, true));

    [Fact]
    public void OtherRepositoriesPlatformsAndFirstTimeInstallsDoNotShowAnUpdateIndicator()
    {
        EditorRelease[] releases = [Release("v2.0.0")];
        Assert.Null(Launcher.FindEditorUpdate(releases, [], Repository, Platform, true));
        Assert.Null(Launcher.FindEditorUpdate(releases, [Installed("v1.0.0", "someone/another-editor"), Installed("v1.0.0", platform: "linux-x64")],
            Repository, Platform, true));
    }
}
