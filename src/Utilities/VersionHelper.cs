using System.Text.RegularExpressions;

namespace Prowl.Launcher;

/// <summary>Shared release precedence, with support for historical Prowl editor tags and preview-N releases.</summary>
internal static class VersionHelper
{
    internal const string Pattern = @"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?";

    internal static bool TryParse(string? value, out ReleaseVersion version, bool editorTag = false)
    {
        version = null!;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        if (editorTag)
        {
            if (value.StartsWith('v'))
            {
                value = value[1..];
            }
            int suffix = value.IndexOfAny(['-', '+']);
            string core = suffix < 0 ? value : value[..suffix];
            if (core.Count(character => character == '.') == 1)
            {
                value = core + ".0" + (suffix < 0 ? "" : value[suffix..]);
            }
        }
        if (!Regex.IsMatch(value, $@"\A{Pattern}\z", RegexOptions.CultureInvariant))
        {
            return false;
        }
        string[] parts = value.Split('+')[0].Split('-', 2);
        // These published Prowl spellings mean the same thing as preview.N, alpha.N, beta.N and rc.N.
        string preview = parts.Length == 1 ? "" : Regex.Replace(parts[1],
            @"\A(preview|alpha|beta|rc)-(?=[0-9]+(?:\.|\z))", "$1.");
        string[] identifiers = preview.Length == 0 ? [] : preview.Split('.');
        if (identifiers.Any(identifier => IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0'))
        {
            return false;
        }
        version = new ReleaseVersion(parts[0].Split('.'), identifiers);
        return true;
    }

    internal static bool IsNewer(string candidate, string current) => TryParse(candidate, out ReleaseVersion next)
        && TryParse(current, out ReleaseVersion installed) && next.CompareTo(installed) > 0;

    internal static bool IsPreview(string value) => TryParse(value, out ReleaseVersion version) && version.IsPrerelease;

    internal static bool EquivalentEditorTags(string? left, string? right) => TryParse(left, out ReleaseVersion a, true)
        && TryParse(right, out ReleaseVersion b, true) && a.CompareTo(b) == 0;

    internal static bool IsNumeric(string value) => value.All(character => character is >= '0' and <= '9');
}

internal sealed class ReleaseVersion(string[] core, string[] prerelease) : IComparable<ReleaseVersion>
{
    internal bool IsPrerelease => prerelease.Length != 0;

    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null)
        {
            return 1;
        }
        for (int i = 0; i < core.Length; i++)
        {
            int comparison = CompareNumbers(core[i], other.Core[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        if (!IsPrerelease || !other.IsPrerelease)
        {
            return other.IsPrerelease.CompareTo(IsPrerelease);
        }
        for (int i = 0; i < Math.Min(prerelease.Length, other.Prerelease.Length); i++)
        {
            string left = prerelease[i], right = other.Prerelease[i];
            bool leftNumeric = VersionHelper.IsNumeric(left), rightNumeric = VersionHelper.IsNumeric(right);
            int comparison = leftNumeric && rightNumeric ? CompareNumbers(left, right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(left, right);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    private string[] Core => core;
    private string[] Prerelease => prerelease;
    // Compare decimal strings directly so release numbers cannot overflow an integer type.
    private static int CompareNumbers(string left, string right) => left.Length != right.Length
        ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
}
