namespace Prowl.Launcher;

public sealed class Settings
{
    public bool CloseOnEditorLaunch
    {
        get;
        set;
    }
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
    public string ProwlRepository
    {
        get;
        set;
    } = GitHubRepositoryHelper.DefaultProwl;
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
    public string? DefaultEditorKey
    {
        get;
        set;
    }
    public string? LauncherExecutable
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
