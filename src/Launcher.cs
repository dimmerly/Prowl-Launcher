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

    private readonly HttpClient _http = new() { Timeout = Constants.Network.DownloadTimeout };

    private readonly Navigation _navigation = new(initialTab);
    private readonly LauncherPreviews _previews = previews ?? new LauncherPreviews();

    private FontFile _bold = null!;
    private Dialogs _dialogs = null!;
    private FontFile _font = null!;
    private InstallationDialog _installation = null!;
    private LaunchView _launchView = null!;
    private OperationPanel _operationPanel = null!;
    private ProjectsView _projects = null!;
    private SettingsView _settings = null!;
    private Sidebar _sidebar = null!;
    private TitleBar _titleBar = null!;
    private Controls _ui = null!;
    private UninstallDialog _uninstall = null!;
    private LauncherUpdateCoordinator _updates = null!;
    private VersionsView _versions = null!;

    // Exposed for the E2E tests, which drive the real launcher.
    internal OperationCoordinator Operations
    {
        get;
        private set;
    } = null!;

    internal NotificationCenter Notifications
    {
        get;
        private set;
    } = null!;

    internal EditorCoordinator Editors
    {
        get;
        private set;
    } = null!;

    internal NewsSection News
    {
        get;
        private set;
    } = null!;

    internal SamplesView Samples
    {
        get;
        private set;
    } = null!;

    internal LogsDialog Logs
    {
        get;
        private set;
    } = null!;

    internal LauncherAppearance Appearance
    {
        get;
        private set;
    } = null!;

    private bool Capturing => screenshot != null;

    private void Initialize()
    {
        store.BindToCurrentThread();
        _font = Fonts.Load("Geist-Regular.ttf");
        _bold = Fonts.Load("Geist-Bold.ttf");
        Appearance = new LauncherAppearance(store.Home, _font, _bold);
        InitializeLocalization();

        CenterWindow();
        _window.IsVisible = !Capturing;

        CreateComponents();
        InitializeServices();
        ShowPreviewChangelog();
        _installation.Pending =
            _previews.Installation || (!Capturing && LauncherInstallationService.ShouldOffer(store));

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
            _ = Editors.RefreshInBackgroundAsync(_background.Token);
            if (!Capturing)
            {
                _ = _updates.CheckInBackgroundAsync();
            }
        }
        else if (!Capturing)
        {
            Notifications.Notify("launcher.network.offline", "launcher.network.offline_description", ToastType.Info);
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
        Notifications = new NotificationCenter(Appearance);
        Operations = new OperationCoordinator(Notifications, LogError);
        _ui = new Controls(Appearance, _font, _bold, Operations,
            () => new Float2(_window.FramebufferSize.X / ModalScale, _window.FramebufferSize.Y / ModalScale));
        _dialogs = new Dialogs(_ui);

        ProjectBackupService backups = new(store);
        Editors = new EditorCoordinator(store, _http, new GitHubReleasesService(_http, store),
            new EditorInstallerService(_http, store), backups, Operations, _dialogs, Notifications, LogError);
        _updates = new LauncherUpdateCoordinator(_http, store, Operations, _dialogs, Notifications, LogError,
            !offline && !Capturing);

        _titleBar = new TitleBar(store, _ui, _window, _windowFrame != null);
        _sidebar = new Sidebar(store, _ui, _navigation, Editors);
        _operationPanel = new OperationPanel(_ui);
        _installation = new InstallationDialog(store, _ui, _previews.Installation);
        _uninstall = new UninstallDialog(store, _ui) { Pending = uninstall && !Capturing };
        Logs = new LogsDialog(_ui, Editors);
        News = new NewsSection(store, _ui, _http, LogError, _background, offline, Capturing);
        _projects = new ProjectsView(store, _ui, _dialogs, Editors, Notifications, Logs,
            new BackupsDialog(store, _ui, _dialogs, backups, Notifications),
            new NewProjectView(store, _ui, _dialogs, Editors, Notifications, _previews.NewProject));
        _versions = new VersionsView(store, _ui, _dialogs, Editors);
        Samples = new SamplesView(store, _ui, Notifications, new SampleService(_http, store), LogError, _background,
            offline, Capturing);
        _settings = new SettingsView(store, _ui, Notifications, Editors, _updates, _installation, _uninstall,
            _background);
        _launchView = new LaunchView(_ui, _navigation, Editors, Logs, _titleBar);

        Operations.Finished += Editors.ReloadInstalled;
        Operations.CloseRequested += ForceClose;
        Editors.SessionStarted += _ =>
        {
            Logs.Reset();
            _navigation.Tab = LauncherTab.Launch;
        };
    }

    private void InitializeServices()
    {
        try
        {
            Editors.RecoverInterruptedOperations();
        }
        catch (IOException error)
        {
            LogError(error);
        }

        store.ImportRecentProjects();
        Editors.ReloadInstalled();

        // A newly downloaded child starts before activation. A forwarded active copy
        // can clean up immediately, even though it also uses a startup health check.
        if (LauncherStartupService.ReadyPipe == null
            || (store.Settings.LauncherExecutable is { } active && Environment.ProcessPath is { } current
                                                                && FileSystemHelper.PathsEqual(active, current)))
        {
            _updates.Cleanup();
        }

        Editors.LoadCachedReleases();
    }

    /// <summary>
    ///     Adds and opens a project folder or .prowl file opened from outside the launcher: the command line, a file
    ///     association or a drop onto the window.
    /// </summary>
    private void OpenExternalProject(string path)
    {
        if (Operations.Busy)
        {
            Notifications.Notify("launcher.projects.busy", Path.GetFileName(path), ToastType.Warning);
            return;
        }

        _navigation.Tab = LauncherTab.Projects;
        Operations.Start(async token =>
        {
            Project project = store.AddProject(path);
            await Editors.OpenProjectAsync(project, token);
        }, "launcher.projects.opening");
    }

    /// <summary>
    ///     Polls console sessions each frame. When the selected editor becomes ready the launcher either closes
    ///     (if configured) or returns from the launch view.
    /// </summary>
    private void RefreshEditorConsoles()
    {
        foreach (EditorConsoleSession session in Editors.Sessions)
        {
            session.Refresh();
            if (session.State.Ready == null || session.ReadyHandled || Operations.Busy)
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

            if (_navigation.Tab == LauncherTab.Launch && Editors.SelectedSession == session)
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
        Editors?.Dispose();
        Samples?.Dispose();
        News?.Dispose();
        _background.Dispose();
        _updates?.Dispose();
        Operations?.Dispose();
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
