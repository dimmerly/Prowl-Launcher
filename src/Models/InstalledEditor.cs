using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

public sealed record InstalledEditor(
    long ReleaseId,
    string Tag,
    [property: JsonPropertyName("Runtime")] string Platform,
    string ExecutableRelativePath,
    DateTimeOffset InstalledAt
)
{
    public string? SourceCommit { get; init; }
    public string? EditorVersion { get; init; }
    [JsonIgnore]
    public bool IsMain => ReleaseId == 0 && (Tag == "main"
        || SourceCommit is { Length: 40 } && Regex.IsMatch(SourceCommit, @"\A[0-9a-f]{40}\z")
        && (Tag == "main-" + SourceCommit || Tag == "main-" + SourceCommit[..7]));

    // Metadata without provenance belongs to the historical default repository.
    public string Repository
    {
        get;
        init;
    } = Constants.Defaults.ProwlRepository;
    public string Key
    {
        get
        {
            // Keep the full commit in the folder identity; the version name uses its short form.
            string identity = IsMain && Tag != "main" ? "main-" + SourceCommit : Tag;
            return Repository.Equals(Constants.Defaults.ProwlRepository, StringComparison.OrdinalIgnoreCase)
                ? $"{identity}-{Platform}"
                : $"{identity}-{Platform}-{GitHubRepositoryHelper.CacheKey(Repository)}";
        }
    }

    public override string ToString() => Tag;
}
