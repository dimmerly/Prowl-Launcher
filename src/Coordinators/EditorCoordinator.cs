using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>Editor releases and installations: install, repair, uninstall, build from source and launch.</summary>
sealed class EditorCoordinator(
    LauncherStore store,
    HttpClient http,
    GitHubReleasesService github,
    EditorInstallerService installer,
    OperationCoordinator operations,
    Dialogs dialogs,
    NotificationCenter notifications,
    Action<Exception> logError
) : IDisposable
{
    private readonly List<EditorConsoleSession> _editorSessions = [];
    private IReadOnlyList<InstalledEditor> _installed = [];
    private IReadOnlyList<EditorRelease> _releases = [];
    private EditorConsoleSession? _selectedEditorSession;

    public IReadOnlyList<InstalledEditor> Installed => _installed;
    public IReadOnlyList<EditorRelease> Releases => _releases;
    public bool IncludePrereleases { get; set; } = Constants.Defaults.EditorPrereleases;

    /// <summary>Every editor started from this launcher, oldest first.</summary>
    public IReadOnlyList<EditorConsoleSession> Sessions => _editorSessions;

    /// <summary>The most recently launched editor.</summary>
    public EditorConsoleSession? SelectedSession => _selectedEditorSession;

    /// <summary>Raised when an editor process has been started, before it reports ready.</summary>
    public event Action<EditorConsoleSession>? SessionStarted;

    public void Dispose()
    {
        foreach (EditorConsoleSession session in _editorSessions)
        {
            session.Dispose();
        }
    }

    /// <summary>Finishes or rolls back installs that a previous launcher left half done.</summary>
    public void RecoverInterruptedOperations() => installer.RecoverInterruptedOperations();

    public void ReloadInstalled() => _installed = store.InstalledEditors()
        .Where(e => e.Platform == Platform.Identifier)
        .ToArray();

    public InstalledEditor? FindInstalled(string? key) => _installed.FirstOrDefault(editor => editor.Key == key);

    /// <summary>Throws if <paramref name="editor" />'s installation can't be launched.</summary>
    public void Validate(InstalledEditor editor) => _ = installer.LaunchInfo(editor);

    #region Releases

    public void LoadCachedReleases() => _releases = GitHubReleasesService.ReadCache(store, store.Settings.ProwlRepository) ?? [];

    /// <summary>Forgets releases from a previous repository.</summary>
    public void ClearReleases() => _releases = [];

    public EditorRelease? NewerRelease() => FindEditorUpdate(_releases, _installed,
        store.Settings.ProwlRepository, Platform.Identifier, IncludePrereleases);

    /// <summary>The newest release newer than every installed editor from <paramref name="repository" />, or null.</summary>
    internal static EditorRelease? FindEditorUpdate(IEnumerable<EditorRelease> releases,
        IEnumerable<InstalledEditor> installed, string repository, string platform, bool includePrereleases)
    {
        ReleaseVersion? current = null;
        IEnumerable<InstalledEditor> fromRepository = installed.Where(editor =>
            editor.Platform == platform && editor.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));
        foreach (InstalledEditor editor in fromRepository)
        {
            if (VersionHelper.TryParse(editor.Tag, out ReleaseVersion version, true)
                && (current == null || version.CompareTo(current) > 0))
            {
                current = version;
            }
        }
        if (current == null)
        {
            return null;
        }

        EditorRelease? update = null;
        IEnumerable<EditorRelease> candidates = releases.Where(release =>
            !release.Draft && (includePrereleases || !release.Preview) && release.AssetFor(platform) != null);
        foreach (EditorRelease release in candidates)
        {
            if (!VersionHelper.TryParse(release.Tag, out ReleaseVersion version, true)
                || !includePrereleases && version.IsPrerelease
                || version.CompareTo(current) <= 0)
            {
                continue;
            }
            update = release;
            current = version;
        }
        return update;
    }

    /// <summary>Startup refresh: no operation panel, no toasts, no effect on interactive controls.</summary>
    public async Task RefreshInBackgroundAsync(CancellationToken token)
    {
        try
        {
            IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(http, store).GetAsync(token);
            if (!token.IsCancellationRequested)
            {
                _releases = releases;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logError(exception);
        }
    }

    public async Task RefreshAsync(CancellationToken token, bool notify = true)
    {
        operations.Detail = "launcher.versions.checking_releases";
        _releases = await github.GetAsync(token);
        if (!notify)
        {
            return;
        }

        if (github.UsedCache)
        {
            notifications.Notify("launcher.network.offline", "launcher.versions.cached_releases", ToastType.Warning);
            return;
        }
        int count = _releases.Count(r => r.AssetFor(Platform.Identifier) != null);
        notifications.Notify("launcher.versions.releases_checked",
            Loc.Get("launcher.versions.available_count", new
            {
                count, platform = Platform.Identifier
            }));
    }

    #endregion

    #region Installations

    public async Task<InstalledEditor> InstallAsync(EditorRelease release, CancellationToken token, bool repair = false, string? repository = null)
    {
        operations.Title = Loc.Get(repair ? "launcher.versions.repairing" : "launcher.versions.installing", new
        {
            version = release.Tag
        });
        operations.ImmediateProgress = true;
        InstalledEditor editor = await installer.InstallAsync(release, Platform.Identifier, operations.Transfer(), token, repository);
        notifications.Notify(repair ? "launcher.versions.repaired" : "launcher.versions.installed",
            Loc.Get("launcher.versions.ready", new
            {
                version = release.Tag
            }));
        ReloadInstalled();
        return editor;
    }

    /// <summary>Reinstalls the exact release an editor came from, looked up in its own repository.</summary>
    public async Task RepairAsync(InstalledEditor editor, CancellationToken token)
    {
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(http, store, editor.Repository).GetAsync(token);
        EditorRelease release = releases.FirstOrDefault(release => release.Id == editor.ReleaseId && release.Tag == editor.Tag)
                                ?? throw new InvalidOperationException(Loc.Get("launcher.errors.release_unavailable"));
        await InstallAsync(release, token, true, editor.Repository);
    }

    public async Task BuildMainAsync(CancellationToken token, string? repository = null)
    {
        operations.ImmediateProgress = true;
        EditorInstallerService.SourceBuildResult result = await installer.InstallMainAsync(Platform.Identifier, operations.Transfer(), token, repository);
        notifications.Notify(result.AlreadyInstalled ? "launcher.versions.main_current" : "launcher.versions.main_ready", result.Editor.ToString());
    }

    public async Task UninstallAsync(InstalledEditor editor, CancellationToken token)
    {
        bool confirmed = await dialogs.ConfirmAsync(
            Loc.Get("launcher.versions.uninstall_title", new
            {
                version = editor.Tag
            }),
            "launcher.versions.uninstall_description",
            "launcher.versions.uninstall",
            token);
        if (!confirmed)
        {
            return;
        }

        await Task.Run(() => installer.Uninstall(editor), token);
        notifications.Notify("launcher.versions.uninstalled", Loc.Get("launcher.versions.uninstalled_description", new
        {
            version = editor.Tag
        }));
    }

    public void SetDefault(InstalledEditor editor, bool enabled)
    {
        store.Settings.DefaultEditorKey = enabled ? editor.Key : null;
        store.Save();
        notifications.Notify("launcher.versions.default_updated", enabled ? editor.Tag : "launcher.versions.automatic_selection");
    }

    public Task OpenInstallFolderAsync(CancellationToken token)
    {
        Directory.CreateDirectory(store.VersionsPath);
        ProcessHelper.OpenInShell(store.VersionsPath);
        return Task.CompletedTask;
    }

    #endregion

    #region Launching

    /// <summary>
    ///     Opens <paramref name="project" /> in its editor. When that editor isn't installed, uses an installed editor
    ///     for the project's version, or offers to install the matching release first.
    /// </summary>
    public async Task OpenProjectAsync(Project project, CancellationToken token)
    {
        if (!Directory.Exists(project.Path))
        {
            throw new DirectoryNotFoundException(Loc.Get("launcher.errors.project_folder_missing"));
        }

        InstalledEditor? editor = FindInstalled(project.EditorKey);
        if (editor == null && ProjectVersion.TryParse(LauncherStore.ReadProjectVersion(project.Path), out ProjectVersion version))
        {
            editor = _installed.FirstOrDefault(e => !e.IsMain && ProjectVersion.TryParse(e, out ProjectVersion v) && v.CompareTo(version) == 0);
            if (editor == null)
            {
                EditorRelease release = await FindReleaseAsync(version, token)
                                        ?? throw new InvalidOperationException(Loc.Get("launcher.errors.no_matching_editor", new
                                        {
                                            version = version.ToString()
                                        }));
                bool accepted = await dialogs.ConfirmAsync(
                    Loc.Get("launcher.projects.install_editor_title", new
                    {
                        version = release.Tag
                    }),
                    Loc.Get("launcher.projects.install_editor_description", new
                    {
                        project = project.Name, version = release.Tag
                    }),
                    "launcher.projects.install_and_open",
                    token);
                if (!accepted)
                {
                    return;
                }
                editor = await InstallAsync(release, token);
            }
        }
        if (editor == null)
        {
            throw new InvalidOperationException(Loc.Get("launcher.errors.choose_installed_editor"));
        }

        if (project.EditorKey != editor.Key)
        {
            project.EditorKey = editor.Key;
            store.Save();
        }
        await LaunchAsync(editor, project, token);
    }

    /// <summary>The installable release for <paramref name="version" />, checking GitHub when the cached list has none.</summary>
    private async Task<EditorRelease?> FindReleaseAsync(ProjectVersion version, CancellationToken token)
    {
        EditorRelease? Match() => _releases.FirstOrDefault(release => !release.Draft && release.AssetFor(Platform.Identifier) != null
                                                                      && ProjectVersion.TryParse(release.Tag, out ProjectVersion v)
                                                                      && v.CompareTo(version) == 0);

        if (Match() is {} cached)
        {
            return cached;
        }
        operations.Detail = "launcher.versions.checking_releases";
        _releases = await github.GetAsync(token);
        return Match();
    }

    /// <summary>
    ///     Starts the editor through a console session and raises <see cref="SessionStarted" />. Asks first when the
    ///     editor would upgrade the project or can't open it, and offers the SDK download when the editor's .NET SDK
    ///     is missing.
    /// </summary>
    public async Task LaunchAsync(InstalledEditor editor, Project? project, CancellationToken token)
    {
        if (project != null && !await ConfirmProjectVersionAsync(project, editor, token))
        {
            return;
        }

        int sdk = installer.RequiredSdkMajor(editor);
        if (!await EditorInstallerService.HasSdkAsync(sdk, token) && !await ConfirmMissingSdkAsync(sdk, token))
        {
            return;
        }

        EditorConsoleSession session = EditorConsoleService.Start(installer.LaunchInfo(editor, project?.Path),
            store.Home, project?.Name ?? editor.Tag, editor.Tag);
        _editorSessions.Add(session);
        _selectedEditorSession = session;
        SessionStarted?.Invoke(session);
        await session.WaitForStartAsync(token);
        if (project != null)
        {
            project.LastOpened = DateTimeOffset.UtcNow;
            store.Save();
        }

        session.CloseWhenReady = store.Settings.CloseOnEditorLaunch;
    }

    /// <summary>
    ///     A newer editor migrates the project (backing it up first), after which older editors can't open it; an
    ///     older editor refuses the project. Both ask first. Unknown versions on either side open without asking.
    /// </summary>
    private async Task<bool> ConfirmProjectVersionAsync(Project project, InstalledEditor editor, CancellationToken token)
    {
        if (!ProjectVersion.TryParse(LauncherStore.ReadProjectVersion(project.Path), out ProjectVersion saved)
            || !ProjectVersion.TryParse(editor, out ProjectVersion target)
            || target.CompareTo(saved) == 0)
        {
            return true;
        }

        object values = new
        {
            project = project.Name, saved = saved.ToString(), editor = target.ToString()
        };
        bool upgrade = target.CompareTo(saved) > 0;
        return await dialogs.ConfirmAsync(
            Loc.Get(upgrade ? "launcher.projects.upgrade_title" : "launcher.projects.downgrade_title", values),
            Loc.Get(upgrade ? "launcher.projects.upgrade_description" : "launcher.projects.downgrade_description", values),
            upgrade ? "launcher.projects.upgrade" : "launcher.projects.open_anyway",
            token);
    }

    private Task<bool> ConfirmMissingSdkAsync(int sdk, CancellationToken token) => dialogs.ConfirmAsync(
        Loc.Get("launcher.sdk.required_title", new
        {
            sdk
        }),
        Loc.Get("launcher.sdk.required_description", new
        {
            sdk
        }),
        "launcher.sdk.open_anyway",
        token,
        $"https://dotnet.microsoft.com/download/dotnet/{sdk}.0");

    #endregion
}
