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
    bool uninstallPreview = false
)
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private readonly CancellationTokenSource _backgroundCancellation = new();
    private GitHubReleasesService _github = null!;
    private EditorInstallerService _installer = null!;
    private LauncherAppearance _appearance = null!;

    private void Initialize()
    {

        _font = LoadFont("Geist-Regular.ttf");
        _bold = LoadFont("Geist-Bold.ttf");
        _appearance = new LauncherAppearance(store.Home, _font, _bold);

        string locale = store.Settings.Locale ?? EditorPreferences.Locale() ?? "en";
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

        CenterWindow();
        _window.IsVisible = screenshot == null;

        _github = new GitHubReleasesService(_http, store);
        _installer = new EditorInstallerService(_http, store);
        store.ImportRecentProjects();
        ReloadInstalled();
        string cachePath = GitHubReleasesService.CachePath(store, store.Settings.ProwlRepository);
        _releases = LauncherStore.ReadJson<List<EditorRelease>>(cachePath) ?? [];

        ShowPreviewChangelog();
        
        if (StartPreviewOperation())
        {
            return;
        }

        if (!offline)
        {
            _ = RefreshInBackgroundAsync();
        }
        else if (screenshot == null)
        {
            Notify("launcher.network.offline", "launcher.network.offline_description", ToastType.Info);
        }
    }

    private void Closing()
    {
        _sampleThumbnails.Dispose();
        _backgroundCancellation.Cancel();
        _operation?.Dispose();
        _http.Dispose();
        _backgroundCancellation.Dispose();
        
    }
}
