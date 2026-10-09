using System.Reflection;

using Prowl.Scribe;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private FontFile _font = null!;
    private FontFile _bold = null!;

    private static FontFile LoadFont(string name)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        return new FontFile(stream);
    }

    private void LoadLanguageFonts()
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
        foreach (FontFile font in _paper.EnumerateSystemFonts()
            .Where(f => families.Contains(f.FamilyName, StringComparer.OrdinalIgnoreCase) && f.Style == FontStyle.Regular))
        {
            _paper.AddFallbackFont(font);
        }
    }
}
