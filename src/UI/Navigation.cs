namespace Prowl.Launcher;

/// <summary>The page shown in the main area. <see cref="Launch" /> is the full-screen editor launch view.</summary>
public enum LauncherTab
{
    Projects,
    Versions,
    Settings,
    Samples,
    Launch
}

/// <summary>Which page is showing. Shared by the sidebar, the views that switch pages and the window shell.</summary>
sealed class Navigation(LauncherTab initial)
{
    public LauncherTab Tab { get; set; } = initial;
}
