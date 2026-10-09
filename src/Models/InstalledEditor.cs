using System.Text.Json.Serialization;

namespace Prowl.Launcher;

public sealed record InstalledEditor(
    long ReleaseId,
    string Tag,
    [property: JsonPropertyName("Runtime")] string Platform,
    string ExecutableRelativePath,
    DateTimeOffset InstalledAt
)
{
    // Metadata without provenance belongs to the historical default repository.
    public string Repository
    {
        get;
        init;
    } = GitHubRepositoryHelper.DefaultProwl;
    public string Key => Repository.Equals(GitHubRepositoryHelper.DefaultProwl, StringComparison.OrdinalIgnoreCase)
        ? $"{Tag}-{Platform}"
        : $"{Tag}-{Platform}-{GitHubRepositoryHelper.CacheKey(Repository)}";

    public override string ToString() => Tag;
}
