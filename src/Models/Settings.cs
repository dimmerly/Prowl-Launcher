namespace Prowl.Launcher;

public sealed class Settings
{
    public bool InstallationPromptHandled
    {
        get;
        set;
    }
    public string? InstalledLauncherExecutable
    {
        get;
        set;
    }
    public string? LauncherExecutable
    {
        get;
        set;
    }

    public bool CloseOnEditorLaunch
    {
        get;
        set;
    } = Constants.Defaults.CloseOnEditorLaunch;
    public string? DefaultEditorKey
    {
        get;
        set;
    }
    public string ProwlRepository
    {
        get;
        set;
    } = Constants.Defaults.ProwlRepository;
    public string LauncherRepository
    {
        get;
        set;
    } = Constants.Defaults.LauncherRepository;
    public bool LauncherPrereleases
    {
        get;
        set;
    } = Constants.Defaults.LauncherPrereleases;
    public bool LauncherAutoUpdate
    {
        get;
        set;
    } = Constants.Defaults.LauncherAutoUpdate;
    public Dictionary<string, bool> DismissedLauncherReleases
    {
        get;
        set;
    } = [];

    public bool ShowFps
    {
        get;
        set;
    } = Constants.Defaults.ShowFps;
    public string? Locale
    {
        get;
        set;
    }
    public float UiScale
    {
        get;
        set;
    } = Constants.Defaults.UiScale;
    public string? LogoColor
    {
        get;
        set;
    }

    public bool RecentProjectsImported
    {
        get;
        set;
    }
    public bool RecentProjectMetadataImported
    {
        get;
        set;
    }
    public List<Project> Projects
    {
        get;
        set;
    } = [];
}
