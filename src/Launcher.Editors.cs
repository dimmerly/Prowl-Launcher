using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>Editor releases and installations: install, repair, uninstall, build from source and launch.</summary>
public sealed partial class Launcher
{

    private readonly List<EditorConsoleSession> _editorSessions = [];
    private bool _includeEditorPrereleases = Constants.Defaults.EditorPrereleases;
    private IReadOnlyList<InstalledEditor> _installed = [];
    private IReadOnlyList<EditorRelease> _releases = [];
    private EditorConsoleSession? _selectedEditorSession;

    private void ReloadInstalled() => _installed = store.InstalledEditors()
        .Where(e => e.Platform == Platform.Identifier)
        .ToArray();

    #region Releases

    private EditorRelease? NewerEditorRelease() => FindEditorUpdate(_releases, _installed,
        store.Settings.ProwlRepository, Platform.Identifier, _includeEditorPrereleases);

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
    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(_http, store).GetAsync(_backgroundCancellation.Token);
            if (!_backgroundCancellation.IsCancellationRequested)
            {
                _releases = releases;
            }
        }
        catch (OperationCanceledException) when (_backgroundCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogError(exception);
        }
    }

    private async Task RefreshAsync(CancellationToken token, bool notify = true)
    {
        _operationDetail = "launcher.versions.checking_releases";
        _releases = await _github.GetAsync(token);
        if (!notify)
        {
            return;
        }

        if (_github.UsedCache)
        {
            Notify("launcher.network.offline", "launcher.versions.cached_releases", ToastType.Warning);
            return;
        }
        int count = _releases.Count(r => r.AssetFor(Platform.Identifier) != null);
        Notify("launcher.versions.releases_checked",
            Loc.Get("launcher.versions.available_count", new
            {
                count, platform = Platform.Identifier
            }));
    }

    #endregion

    #region Installations

    private async Task InstallAsync(EditorRelease release, CancellationToken token, bool repair = false, string? repository = null)
    {
        _operationTitle = Loc.Get(repair ? "launcher.versions.repairing" : "launcher.versions.installing", new
        {
            version = release.Tag
        });
        _immediateProgress = true;
        await _installer.InstallAsync(release, Platform.Identifier, Transfer(), token, repository);
        Notify(repair ? "launcher.versions.repaired" : "launcher.versions.installed",
            Loc.Get("launcher.versions.ready", new
            {
                version = release.Tag
            }));
    }

    /// <summary>Reinstalls the exact release an editor came from, looked up in its own repository.</summary>
    private async Task RepairEditorAsync(InstalledEditor editor, CancellationToken token)
    {
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(_http, store, editor.Repository).GetAsync(token);
        EditorRelease release = releases.FirstOrDefault(release => release.Id == editor.ReleaseId && release.Tag == editor.Tag)
                                ?? throw new InvalidOperationException(Loc.Get("launcher.errors.release_unavailable"));
        await InstallAsync(release, token, true, editor.Repository);
    }

    private async Task BuildMainAsync(CancellationToken token, string? repository = null)
    {
        _immediateProgress = true;
        EditorInstallerService.SourceBuildResult result = await _installer.InstallMainAsync(Platform.Identifier, Transfer(), token, repository);
        Notify(result.AlreadyInstalled ? "launcher.versions.main_current" : "launcher.versions.main_ready", result.Editor.ToString());
    }

    private async Task UninstallEditorAsync(InstalledEditor editor, CancellationToken token)
    {
        bool confirmed = await ConfirmAsync(
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

        await Task.Run(() => _installer.Uninstall(editor), token);
        Notify("launcher.versions.uninstalled", Loc.Get("launcher.versions.uninstalled_description", new
        {
            version = editor.Tag
        }));
    }

    private void SetDefaultEditor(InstalledEditor editor, bool enabled)
    {
        store.Settings.DefaultEditorKey = enabled ? editor.Key : null;
        store.Save();
        Notify("launcher.versions.default_updated", enabled ? editor.Tag : "launcher.versions.automatic_selection");
    }

    private Task OpenInstallFolderAsync(CancellationToken token)
    {
        Directory.CreateDirectory(store.VersionsPath);
        ProcessHelper.OpenInShell(store.VersionsPath);
        return Task.CompletedTask;
    }

    #endregion

    #region Launching

    /// <summary>
    ///     Starts the editor through a console session and switches to the launch view. Offers the SDK download
    ///     first when the editor's .NET SDK is missing.
    /// </summary>
    private async Task LaunchAsync(InstalledEditor editor, Project? project, CancellationToken token)
    {
        int sdk = _installer.RequiredSdkMajor(editor);
        if (!await EditorInstallerService.HasSdkAsync(sdk, token) && !await ConfirmMissingSdkAsync(sdk, token))
        {
            return;
        }

        EditorConsoleSession session = EditorConsoleService.Start(_installer.LaunchInfo(editor, project?.Path),
            store.Home, project?.Name ?? editor.Tag, editor.Tag);
        _editorSessions.Add(session);
        _selectedEditorSession = session;
        _logsSearch = "";
        _logsProblemsOnly = false;
        _tab = LauncherTab.Launch;
        await session.WaitForStartAsync(token);
        if (project != null)
        {
            project.LastOpened = DateTimeOffset.UtcNow;
            store.Save();
        }

        session.CloseWhenReady = store.Settings.CloseOnEditorLaunch;
    }

    private Task<bool> ConfirmMissingSdkAsync(int sdk, CancellationToken token) => ConfirmAsync(
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

    /// <summary>
    ///     Polls console sessions each frame. When the selected editor becomes ready the launcher either closes
    ///     (if configured) or returns from the launch view.
    /// </summary>
    private void RefreshEditorConsoles()
    {
        foreach (EditorConsoleSession session in _editorSessions)
        {
            session.Refresh();
            if (session.State.Ready == null || session.ReadyHandled || Busy)
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
            if (_tab == LauncherTab.Launch && _selectedEditorSession == session)
            {
                if (Modal.IsOpen)
                {
                    Modal.Pop();
                }
                _tab = LauncherTab.Projects;
            }
        }
    }

    #endregion
}
