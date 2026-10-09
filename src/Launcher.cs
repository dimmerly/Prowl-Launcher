using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher(
    LauncherStore store,
    string? screenshot = null,
    int initialTab = 0,
    bool offline = false,
    bool progressPreview = false,
    bool alertPreview = false,
    bool notesPreview = false,
    bool newProjectPreview = false,
    bool uninstallPreview = false,
    bool installationPreview = false
)
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private readonly CancellationTokenSource _backgroundCancellation = new();
    private readonly CancellationTokenSource _launcherUpdateCancellation = new();

    private GitHubReleasesService _github = null!;
    private EditorInstallerService _installer = null!;
    private SampleService _samples = null!;
    private LauncherAppearance _appearance = null!;

    private void Initialize()
    {
        InitializeAppearance();
        InitializeLocalization();

        CenterWindow();
        _window.IsVisible = screenshot == null;

        InitializeServices();
        ShowPreviewChangelog();
        _showInstallationPrompt = installationPreview || (screenshot == null && LauncherInstallationService.ShouldOffer(store));

        if (StartPreviewOperation())
        {
            return;
        }

        if (!offline)
        {
            _ = RefreshInBackgroundAsync();
            if (screenshot == null) _ = CheckLauncherInBackgroundAsync();
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
        string locale = store.Settings.Locale ?? EditorSettings.Locale() ?? "en";
        if (!LocaleHelper.Codes.Contains(locale))
        {
            locale = "en";
        }

        store.Settings.Locale = locale;
        Loc.Configure(config => config
            .SetFallbackLocale("en")
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

        try { _installer.RecoverInterruptedOperations(); }
        catch (IOException error) { LogError(error); }

        store.ImportRecentProjects();
        ReloadInstalled();

        string cachePath = GitHubReleasesService.CachePath(store, store.Settings.ProwlRepository);
        _releases = LauncherStore.ReadJson<List<EditorRelease>>(cachePath) ?? [];
    }

    private void Closing()
    {
        _sampleThumbnails.Dispose();
        _backgroundCancellation.Cancel();
        _launcherUpdateCancellation.Cancel();
        _operation?.Dispose();
        _http.Dispose();
        _backgroundCancellation.Dispose();
        _launcherUpdateCancellation.Dispose();
    }
}
