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
    public string Key => $"{ReleaseId}-{Platform}";

    public override string ToString() => Tag;
}
