using Xunit;

namespace Prowl.Launcher.Test;

public sealed class ProjectVersionTests
{
    [Theory]
    [InlineData("1.0-preview.5", "1.0-preview.5")]
    [InlineData("1.0", "1.0")]
    [InlineData("1.1a", "1.1a")]
    [InlineData("1.0.0", "1.0")]
    [InlineData("preview-5", "1.0-preview.5")]
    [InlineData("v1.0-preview-4", "1.0-preview.4")]
    [InlineData("v1.0.0-preview-4", "1.0-preview.4")]
    public void ReadsProjectAndEditorSpellings(string text, string expected)
    {
        Assert.True(ProjectVersion.TryParse(text, out ProjectVersion version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.0.1")]
    [InlineData("main-abc1234")]
    [InlineData("1.0.1")]
    [InlineData("1.0-beta.1")]
    public void UnknownVersionsAreNotParsed(string? text) => Assert.False(ProjectVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0-preview.4", "1.0-preview.5")]
    [InlineData("1.0-preview.9", "1.0")]
    [InlineData("1.0", "1.0a")]
    [InlineData("1.0z", "1.0aa")]
    [InlineData("1.0a", "1.1-preview.1")]
    [InlineData("1.9", "2.0")]
    public void OrdersLikeTheEditor(string older, string newer)
    {
        Assert.True(ProjectVersion.TryParse(older, out ProjectVersion a));
        Assert.True(ProjectVersion.TryParse(newer, out ProjectVersion b));
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void EditorsUseTheirReportedVersionBeforeTheirTag()
    {
        InstalledEditor editor = new(1, "v1.0-preview-4", "win-x64", "Prowl.Editor.exe", DateTimeOffset.UtcNow)
        {
            EditorVersion = "1.0-preview.5+abc"
        };
        Assert.True(ProjectVersion.TryParse(editor, out ProjectVersion version));
        Assert.Equal("1.0-preview.5", version.ToString());
    }
}
