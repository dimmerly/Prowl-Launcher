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
    } = true;
    public string? DefaultEditorKey
    {
        get;
        set;
    }
    public string ProwlRepository
    {
        get;
        set;
    } = GitHubRepositoryHelper.DefaultProwl;
    public string LauncherRepository
    {
        get;
        set;
    } = GitHubRepositoryHelper.LauncherRepository;
    public bool LauncherPrereleases
    {
        get;
        set;
    } = true;
    public Dictionary<string, bool> DismissedLauncherReleases
    {
        get;
        set;
    } = [];

    public bool ShowFps
    {
        get;
        set;
    }
    public string? Locale
    {
        get;
        set;
    }
    public float UiScale
    {
        get;
        set;
    } = 1;
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
