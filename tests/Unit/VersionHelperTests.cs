using Xunit;

namespace Prowl.Launcher.Test;

public sealed class VersionHelperTests
{
    [Theory]
    [InlineData("1.0.0-preview-9", "1.0.0-preview-10")]
    [InlineData("1.0.0-alpha-9", "1.0.0-alpha-10")]
    [InlineData("1.0.0-beta-9", "1.0.0-beta-10")]
    [InlineData("1.0.0-rc-9", "1.0.0-rc-10")]
    [InlineData("1.0.0-preview.9", "1.0.0-preview-10")]
    [InlineData("1.0.0-preview-9", "1.0.0-preview.10")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1-alpha")]
    [InlineData("1.0.0-preview.9223372036854775808", "1.0.0-preview.100000000000000000000")]
    [InlineData("1.0.0", "999999999999999999999.0.0")]
    [InlineData("1.0.0-feature-9", "1.0.0-feature-abc")]
    public void PrecedenceIsNumericAndSymmetric(string older, string newer)
    {
        Assert.True(VersionHelper.IsNewer(newer, older));
        Assert.False(VersionHelper.IsNewer(older, newer));
    }

    [Theory]
    [InlineData("1.0.0+one", "1.0.0+two")]
    [InlineData("1.0.0-preview-10", "1.0.0-preview.10+build-9")]
    [InlineData("1.0.0-alpha-10", "1.0.0-alpha.10")]
    [InlineData("1.0.0-beta-10", "1.0.0-beta.10")]
    [InlineData("1.0.0-rc-10", "1.0.0-rc.10")]
    public void EquivalentVersionsNeverOfferAnUpdate(string left, string right)
    {
        Assert.False(VersionHelper.IsNewer(left, right));
        Assert.False(VersionHelper.IsNewer(right, left));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0\n")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("١.0.0")]
    [InlineData("1.0.0-preview..1")]
    [InlineData("1.0.0-preview.01")]
    [InlineData("1.0.0-preview-01")]
    [InlineData("1.0.0+")]
    public void InvalidVersionsCannotParticipateInUpdates(string? value)
    {
        Assert.False(VersionHelper.TryParse(value, out _));
        if (value != null)
        {
            Assert.False(VersionHelper.IsNewer(value, "0.0.0"));
            Assert.False(VersionHelper.IsNewer("2.0.0", value));
        }
    }

    [Theory]
    [InlineData("v1.0-preview-10", "1.0.0-preview.10")]
    [InlineData("v1.0.0-beta-10", "1.0.0-beta.10+build")]
    [InlineData("1.0", "v1.0.0")]
    [InlineData("0.0", "v0.0.0")]
    public void EditorTagsUseTheSamePrecedenceWithHistoricalSyntax(string left, string right)
        => Assert.True(VersionHelper.EquivalentEditorTags(left, right));

    [Theory]
    [InlineData("vv1.0.0")]
    [InlineData("unknown")]
    [InlineData("")]
    public void InvalidEditorTagsDoNotMatchEvenThemselves(string tag)
        => Assert.False(VersionHelper.EquivalentEditorTags(tag, tag));

    [Theory]
    [InlineData("1.0.0+build-9", false)]
    [InlineData("1.0.0-preview-10+build", true)]
    [InlineData("unknown-preview", false)]
    public void PreviewDetectionUsesTheParsedVersion(string value, bool expected)
        => Assert.Equal(expected, VersionHelper.IsPreview(value));
}
