using Prowl.Launcher;

using Xunit;

namespace Prowl.Launcher.Test;

public sealed class GitHubRepositoryTests
{
    [Theory]
    [InlineData("https://github.com/Someone/Prowl.git", "Someone/Prowl")]
    [InlineData(" Someone/Prowl ", "Someone/Prowl")]
    [InlineData("https://github.com/Someone/Prowl/", "Someone/Prowl")]
    public void RepositoryInputAcceptsNamesAndGitHubUrls(string value, string expected)
        => Assert.Equal(expected, GitHubRepositoryHelper.Normalize(value));

    [Theory]
    [InlineData("https://example.com/Someone/Prowl")]
    [InlineData("../Prowl")]
    [InlineData("Someone/../Prowl")]
    public void RepositoryInputRejectsInvalidNames(string value)
        => Assert.Throws<InvalidDataException>(() => GitHubRepositoryHelper.Normalize(value));
}
