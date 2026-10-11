using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.Rosetta;
using Prowl.Scribe;
using Prowl.Vector;
namespace Prowl.Launcher;

/// <summary>
///     The launcher application: owns the window, the services and the views, and wires them together. Views and
///     coordinators are separate classes that receive only what they use.
/// </summary>
public sealed partial class Launcher(
    LauncherStore store,
    string? screenshot = null,
    LauncherTab initialTab = LauncherTab.Projects,
    bool offline = false,
    LauncherPreviews? previews = null,
    bool uninstall = false,
    string? openProject = null
)
{
    private readonly BackgroundWork _background = new();
    private readonly HttpClient _http = new()
    {
        Timeout = Constants.Network.DownloadTimeout
    };
    private readonly Navigation _navigation = new(initialTab);
    private readonly LauncherPreviews _previews = previews ?? new LauncherPreviews();

    private LauncherAppearance _appearance = null!;
    private FontFile _bold = null!;
    private Dialogs _dialogs = null!;
    private EditorCoordinator _editors = null!;
    private FontFile _font = null!;
    private InstallationDialog _installation = null!;
    private LaunchView _launchView = null!;
    private LogsDialog _logs = null!;
    private NewsSection _news = null!;
    private NotificationCenter _notifications = null!;
    private OperationPanel _operationPanel = null!;
    private OperationCoordinator _operations = null!;
    private ProjectsView _projects = null!;
    private SamplesView _samples = null!;
    private SettingsView _settings = null!;
    private Sidebar _sidebar = null!;
    private UninstallDialog _uninstall = null!;
    private TitleBar _titleBar = null!;
    private Controls _ui = null!;
    private LauncherUpdateCoordinator _updates = null!;
    private VersionsView _versions = null!;

    // Exposed for the E2E tests, which drive the real launcher.
    internal OperationCoordinator Operations => _operations;
    internal NotificationCenter Notifications => _notifications;
    internal EditorCoordinator Editors => _editors;
    internal NewsSection News => _news;
    internal SamplesView Samples => _samples;
    internal LogsDialog Logs => _logs;
    internal LauncherAppearance Appearance => _appearance;

    private bool Capturing => screenshot != null;

    private void Initialize()
    {
        _font = Fonts.Load("Geist-Regular.ttf");
        _bold = Fonts.Load("Geist-Bold.ttf");
        _appearance = new LauncherAppearance(store.Home, _font, _bold);
        InitializeLocalization();

        CenterWindow();
        _window.IsVisible = !Capturing;

        CreateComponents();
        InitializeServices();
        ShowPreviewChangelog();
        _installation.Pending = _previews.Installation || !Capturing && LauncherInstallationService.ShouldOffer(store);

        if (StartPreviewOperation())
        {
            return;
        }
        if (openProject != null && !Capturing)
        {
            OpenExternalProject(openProject);
        }

        if (!offline)
        {
            _ = _editors.RefreshInBackgroundAsync(_background.Token);
            if (!Capturing)
            {
                _ = _updates.CheckInBackgroundAsync();
            }
        }
        else if (!Capturing)
        {
            _notifications.Notify("launcher.network.offline", "launcher.network.offline_description", ToastType.Info);
        }
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
        Fonts.AddLanguageFallbacks(_paper);
    }

    /// <summary>Builds the services, coordinators and views. Localization must be configured first.</summary>
    private void CreateComponents()
    {
        _notifications = new NotificationCenter(_appearance);
        _operations = new OperationCoordinator(_notifications, LogError);
        _ui = new Controls(_appearance, _font, _bold, _operations,
            () => new Float2(_window.FramebufferSize.X / ModalScale, _window.FramebufferSize.Y / ModalScale));
        _dialogs = new Dialogs(_ui);

        _editors = new EditorCoordinator(store, _http, new GitHubReleasesService(_http, store),
            new EditorInstallerService(_http, store), _operations, _dialogs, _notifications, LogError);
        _updates = new LauncherUpdateCoordinator(_http, store, _operations, _dialogs, _notifications, LogError,
            !offline && !Capturing);

        _titleBar = new TitleBar(store, _ui, _window, _windowFrame != null);
        _sidebar = new Sidebar(store, _ui, _navigation, _editors);
        _operationPanel = new OperationPanel(_ui);
        _installation = new InstallationDialog(store, _ui, _previews.Installation);
        _uninstall = new UninstallDialog(store, _ui)
        {
            Pending = uninstall && !Capturing
        };
        _logs = new LogsDialog(_ui, _editors);
        _news = new NewsSection(store, _ui, _http, LogError, _background, offline, Capturing);
        _projects = new ProjectsView(store, _ui, _dialogs, _editors, _notifications, _logs,
            new NewProjectView(store, _ui, _dialogs, _editors, _notifications, _previews.NewProject));
        _versions = new VersionsView(store, _ui, _dialogs, _editors);
        _samples = new SamplesView(store, _ui, _notifications, new SampleService(_http, store), LogError, _background, offline, Capturing);
        _settings = new SettingsView(store, _ui, _notifications, _editors, _updates, _uninstall, _background);
        _launchView = new LaunchView(_ui, _navigation, _editors, _logs, _titleBar);

        _operations.Finished += _editors.ReloadInstalled;
        _operations.CloseRequested += ForceClose;
        _editors.SessionStarted += _ =>
        {
            _logs.Reset();
            _navigation.Tab = LauncherTab.Launch;
        };
    }

    private void InitializeServices()
    {
        try
        {
            _editors.RecoverInterruptedOperations();
        }
        catch (IOException error)
        {
            LogError(error);
        }

        store.ImportRecentProjects();
        _editors.ReloadInstalled();

        // A newly downloaded child starts before activation. A forwarded active copy
        // can clean up immediately, even though it also uses a startup health check.
        if (LauncherStartupService.ReadyPipe == null
            || store.Settings.LauncherExecutable is {} active && Environment.ProcessPath is {} current
                                                              && FileSystemHelper.PathsEqual(active, current))
        {
            _updates.Cleanup();
        }

        _editors.LoadCachedReleases();
    }

    /// <summary>
    ///     Adds and opens a project folder or .prowl file opened from outside the launcher: the command line, a file
    ///     association or a drop onto the window.
    /// </summary>
    private void OpenExternalProject(string path)
    {
        if (_operations.Busy)
        {
            _notifications.Notify("launcher.projects.busy", Path.GetFileName(path), ToastType.Warning);
            return;
        }
        _navigation.Tab = LauncherTab.Projects;
        _operations.Start(async token =>
        {
            Project project = store.AddProject(path);
            await _editors.OpenProjectAsync(project, token);
        }, "launcher.projects.opening");
    }

    /// <summary>
    ///     Polls console sessions each frame. When the selected editor becomes ready the launcher either closes
    ///     (if configured) or returns from the launch view.
    /// </summary>
    private void RefreshEditorConsoles()
    {
        foreach (EditorConsoleSession session in _editors.Sessions)
        {
            session.Refresh();
            if (session.State.Ready == null || session.ReadyHandled || _operations.Busy)
            {
                continue;
            }
            session.ReadyHandled = true;
            if (session.CloseWhenReady && session.Active)
            {
                session.CloseWhenReady = false;
                ForceClose();
                return;
            }
            if (_navigation.Tab == LauncherTab.Launch && _editors.SelectedSession == session)
            {
                if (Modal.IsOpen)
                {
                    Modal.Pop();
                }
                _navigation.Tab = LauncherTab.Projects;
            }
        }
    }

    private void LogError(Exception exception) => LauncherLog.Append(store.Home, exception);

    private void Closing()
    {
        _editors?.Dispose();
        _samples?.Dispose();
        _news?.Dispose();
        _background.Dispose();
        _updates?.Dispose();
        _operations?.Dispose();
        _http.Dispose();
    }
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
