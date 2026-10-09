using System.Xml.Linq;

using Prowl.OrigamiUI;

namespace Prowl.Launcher;

static class LauncherIcons
{
    public static readonly SvgIcon FilledStar = new(
        "M8 1.8l1.9 3.9 4.3.6-3.1 3 .7 4.3L8 11.6 4.2 13.6l.7-4.3-3.1-3 4.3-.6z",
        fill: true );

    public static readonly IOrigamiIcon GitHub = LoadSvgIcon("github");
    public static readonly IOrigamiIcon Discord = LoadSvgIcon("discord");
    public static readonly IOrigamiIcon Settings = LoadSvgIcon("settings");
    public static readonly IOrigamiIcon Minimize = LoadSvgIcon("minimize");
    public static readonly IOrigamiIcon ProwlLogo = LoadSvgIcon("prowl");

    private static IOrigamiIcon LoadSvgIcon(string name)
    {
        using Stream stream = typeof( LauncherIcons ).Assembly.GetManifestResourceStream($"Prowl.Launcher.Icons.{name}.svg")
                              ?? throw new InvalidOperationException($"Missing SVG icon: {name}");
        XElement svg = XElement.Load(stream);
        float[] viewBox = svg.Attribute("viewBox")!.Value.Split(' ')
            .Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        string path = string.Join(" ", svg.Descendants()
            .Where(element => element.Name.LocalName == "path")
            .Select(element => element.Attribute("d")!.Value));
        return new SvgPathIcon(path, viewBox[2], viewBox[3]);
    }

    public static readonly SvgIcon ExternalLink = new( "M9 2h5v5M14 2L7 9M6 3H3a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V10" );
    public static readonly SvgIcon Refresh = new( "M13 6a5 5 0 1 0 0 4M13 2v4H9" );
    public static readonly SvgIcon Play = new( "M5 2l8 6-8 6z" );
    public static IOrigamiIcon? ForAction(string text) => text switch
    {
        "launcher.projects.add" => OrigamiIconSet.FolderPlus,
        "launcher.projects.new" => OrigamiIconSet.Plus,
        "launcher.projects.create_and_open" => OrigamiIconSet.Plus,
        "launcher.projects.open" or "launcher.versions.launch" or "launcher.samples.run" => Play,
        "launcher.projects.remove" or "launcher.versions.uninstall" => OrigamiIconSet.Trash,
        "launcher.versions.install" => OrigamiIconSet.Download,
        "launcher.versions.repair" or "launcher.versions.refresh" or "launcher.updates.check" => Refresh,
        "launcher.versions.set_default" => OrigamiIconSet.Star,
        "launcher.versions.changelog" => OrigamiIconSet.File,
        "launcher.versions.install_folder" or "launcher.settings.open_data_folder" => OrigamiIconSet.FolderOpen,
        _ => null
    };
}
