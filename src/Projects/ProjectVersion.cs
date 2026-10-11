using System.Text.RegularExpressions;
namespace Prowl.Launcher;

/// <summary>
///     The engine version a project was saved with, as the editor writes it in the project's .prowl file, e.g.
///     <c>1.0-preview.5</c> or the hotfix <c>1.1a</c>. Ordered like the editor's own EngineVersion, so the
///     launcher agrees with the editor about upgrades.
/// </summary>
readonly record struct ProjectVersion(int Major, int Minor, string Hotfix, int Preview) : IComparable<ProjectVersion>
{
    private static readonly Regex Pattern = new(@"\A([0-9]+)\.([0-9]+)(?:\.0)?([a-z]*)(?:-preview[.-]([1-9][0-9]*))?\z");
    private static readonly Regex LegacyPreview = new(@"\Apreview-([1-9][0-9]*)\z");

    /// <summary>
    ///     Parses a known version. Empty text and the <c>0.0.1</c> older editors wrote mean the version is unknown,
    ///     so they return false.
    /// </summary>
    public static bool TryParse(string? text, out ProjectVersion version)
    {
        version = default;
        text = text?.Trim().TrimStart('v');
        if (string.IsNullOrEmpty(text) || text == "0.0.1")
        {
            return false;
        }
        if (LegacyPreview.Match(text) is { Success: true } legacy)
        {
            version = new ProjectVersion(1, 0, "", int.Parse(legacy.Groups[1].Value));
            return true;
        }
        Match match = Pattern.Match(text.Split('+')[0]);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor))
        {
            return false;
        }
        version = new ProjectVersion(major, minor, match.Groups[3].Value,
            match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out int preview) ? preview : 0);
        return true;
    }

    /// <summary>The version <paramref name="editor" /> saves projects with, from its reported version or its release tag.</summary>
    public static bool TryParse(InstalledEditor editor, out ProjectVersion version) =>
        TryParse(editor.EditorVersion ?? editor.Tag, out version);

    public int CompareTo(ProjectVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }
        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }
        if (Hotfix.Length != other.Hotfix.Length)
        {
            return Hotfix.Length.CompareTo(other.Hotfix.Length);
        }
        int hotfix = string.CompareOrdinal(Hotfix, other.Hotfix);
        return hotfix != 0 ? hotfix : PreviewRank.CompareTo(other.PreviewRank);
    }

    // A release sorts after all of its previews.
    private int PreviewRank => Preview == 0 ? int.MaxValue : Preview;

    public override string ToString() => $"{Major}.{Minor}{Hotfix}" + (Preview > 0 ? $"-preview.{Preview}" : "");
}
