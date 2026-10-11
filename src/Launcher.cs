using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

public sealed partial class Launcher(
    LauncherStore store,
    string? screenshot = null,
    LauncherTab initialTab = LauncherTab.Projects,
    bool offline = false,
    LauncherPreviews? previews = null
)
{
    private readonly CancellationTokenSource _backgroundCancellation = new();
    private readonly HttpClient _http = new()
    {
        Timeout = Constants.Network.DownloadTimeout
    };
    private readonly CancellationTokenSource _launcherUpdateCancellation = new();
    private readonly LauncherPreviews _previews = previews ?? new LauncherPreviews();
    private LauncherAppearance _appearance = null!;

    private GitHubReleasesService _github = null!;
    private EditorInstallerService _installer = null!;
    private SampleService _samples = null!;

    private void Initialize()
    {
        InitializeAppearance();
        InitializeLocalization();

        CenterWindow();
        _window.IsVisible = screenshot == null;

        InitializeServices();
        ShowPreviewChangelog();
        _showInstallationPrompt = _previews.Installation || screenshot == null && LauncherInstallationService.ShouldOffer(store);

        if (StartPreviewOperation())
        {
            return;
        }

        if (!offline)
        {
            _ = RefreshInBackgroundAsync();
            if (screenshot == null)
            {
                _ = CheckLauncherInBackgroundAsync();
            }
        }
        else if (screenshot == null)
        {
            Notify("launcher.network.offline", "launcher.network.offline_description", ToastType.Info);
        }
    }

    private void InitializeAppearance()
    {
        _font = LoadFont("Geist-Regular.ttf");
        _bold = LoadFont("Geist-Bold.ttf");
        _appearance = new LauncherAppearance(store.Home, _font, _bold);
    }

    private void InitializeLocalization()
    {
        string locale = store.Settings.Locale ?? EditorSettings.Locale() ?? Constants.Defaults.Locale;
        if (!LocaleHelper.Codes.Contains(locale))
        {
            locale = Constants.Defaults.Locale;
        }

        store.Settings.Locale = locale;
        Loc.Configure(config => config
            .SetFallbackLocale(Constants.Defaults.Locale)
            .SetLocale(locale)
            .AddProvider(new EmbeddedResourceProvider(Assembly.GetExecutingAssembly(), "Prowl.Launcher.Locale")));
        _newProjectName = Loc.Get("launcher.projects.untitled");
        LoadLanguageFonts();
    }

    private void InitializeServices()
    {
        _github = new GitHubReleasesService(_http, store);
        _installer = new EditorInstallerService(_http, store);
        _samples = new SampleService(_http, store);

        try
        {
            _installer.RecoverInterruptedOperations();
        }
        catch (IOException error)
        {
            LogError(error);
        }

        store.ImportRecentProjects();
        ReloadInstalled();

        // A newly downloaded child starts before activation. A forwarded active copy
        // can clean up immediately, even though it also uses a startup health check.
        if (LauncherStartupService.ReadyPipe == null
            || store.Settings.LauncherExecutable is {} active && Environment.ProcessPath is {} current
                                                              && FileSystemHelper.PathsEqual(active, current))
        {
            CleanupLauncherUpdates();
        }

        _releases = GitHubReleasesService.ReadCache(store, store.Settings.ProwlRepository) ?? [];
    }

    private void Closing()
    {
        foreach (EditorConsoleSession session in _editorSessions)
        {
            session.Dispose();
        }
        _sampleThumbnails.Dispose();
        _newsImages.Dispose();
        _newsThumbnails.Dispose();
        _backgroundCancellation.Cancel();
        _launcherUpdateCancellation.Cancel();
        _operation?.Dispose();
        _http.Dispose();
        _backgroundCancellation.Dispose();
        _launcherUpdateCancellation.Dispose();
    }
}

/// <summary>The page shown in the main area. <see cref="Launch" /> is the full-screen editor launch view.</summary>
public enum LauncherTab
{
    Projects,
    Versions,
    Settings,
    Samples,
    Launch
}

/// <summary>Screenshot and development states the launcher can open directly into.</summary>
public sealed record LauncherPreviews(
    bool Progress = false,
    bool Alert = false,
    bool Notes = false,
    bool NewProject = false,
    bool Uninstall = false,
    bool Installation = false
);
