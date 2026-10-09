using System.Text.RegularExpressions;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed record Sample(string Id)
{
    private string Words => Regex.Replace(Id, "([a-z0-9])([A-Z])|([A-Z])([A-Z][a-z])", "$1$3 $2$4")
        .Replace('_', ' ').Replace('-', ' ');
    private string TranslationId => Words.Replace(' ', '_').ToLowerInvariant();

    public string Name => Translate("name", Words);
    public string Description => Translate("description", "");

    private string Translate(string field, string fallback)
    {
        string key = $"launcher.samples.{TranslationId}_{field}";
        string value = Loc.Get(key);
        return value == key ? fallback : value;
    }
}
