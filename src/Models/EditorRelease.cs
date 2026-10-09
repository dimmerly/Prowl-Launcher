using System.Text.Json.Serialization;

namespace Prowl.Launcher;

public sealed record EditorRelease(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("prerelease")] bool Preview,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("published_at")] DateTimeOffset Published,
    [property: JsonPropertyName("html_url")] string PageUrl,
    [property: JsonPropertyName("body")] string? Notes,
    [property: JsonPropertyName("assets")] List<ReleaseAsset> Assets
)
{
    public ReleaseAsset? AssetFor(string platform) => Assets.FirstOrDefault(a => a.Name.Equals($"Prowl-{Tag}-{platform}.zip", StringComparison.Ordinal));
}
