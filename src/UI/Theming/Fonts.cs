using System.Reflection;

using Prowl.PaperUI;
using Prowl.Scribe;

namespace Prowl.Launcher;

internal static class Fonts
{
    /// <summary>Loads an embedded font, e.g. <c>Geist-Regular.ttf</c>.</summary>
    public static FontFile Load(string name)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        return new FontFile(stream);
    }

    /// <summary>Adds installed CJK-capable system fonts as fallbacks so every language renders.</summary>
    public static void AddLanguageFallbacks(Paper paper)
    {
        string[] families =
        [
            "Yu Gothic UI",
            "Microsoft YaHei",
            "Malgun Gothic",
            "Hiragino Sans",
            "PingFang SC",
            "Apple SD Gothic Neo",
            "Noto Sans CJK SC",
            "Noto Sans CJK",
            "Noto Sans"
        ];
        foreach (FontFile font in paper.EnumerateSystemFonts()
                     .Where(f => families.Contains(f.FamilyName, StringComparer.OrdinalIgnoreCase) &&
                                 f.Style == FontStyle.Regular))
        {
            paper.AddFallbackFont(font);
        }
    }
}
