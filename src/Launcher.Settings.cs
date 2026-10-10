using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private LauncherUpdateCheck? _pendingLauncherUpdate;
    private int _launcherUpdateCheckGeneration;
    private static string LauncherVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private async Task RefreshStorageSizeAsync()
    {
        CancellationToken token = _backgroundCancellation.Token;
        try
        {
            long bytes = await Task.Run(() =>
            {
                long total = 0;
                EnumerationOptions options = new()
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (FileInfo file in new DirectoryInfo(store.Home).EnumerateFiles("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        total += file.Length;
                    }
                    catch (IOException)
                    {
                        // An installation or update may remove files during the scan.
                    }
                }
                return total;
            }, token);
            _launcherStorageSize = bytes >= 1073741824
                ? $"{bytes / 1073741824d:F1} GB"
                : $"{bytes / 1048576d:F1} MB";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _launcherStorageSize = "—";
        }
    }

    private async Task UpdateLauncherAsync(CancellationToken token)
    {
        ++_launcherUpdateCheckGeneration;
        _pendingLauncherUpdate = null;
        LauncherUpdateCheck check = await new LauncherUpdateCheckService(_http, store)
            .CheckAsync(Platform.Identifier, LauncherVersion, false, token);
        await OfferLauncherUpdateAsync(check, token);
    }

    private async Task CheckLauncherInBackgroundAsync()
    {
        int generation = ++_launcherUpdateCheckGeneration;
        _pendingLauncherUpdate = null;
        CancellationToken token = _launcherUpdateCancellation.Token;
        if (!store.Settings.LauncherAutoUpdate || offline || screenshot != null)
        {
            return;
        }
        try
        {
            LauncherUpdateCheck check = await new LauncherUpdateCheckService(_http, store)
                .CheckAsync(Platform.Identifier, LauncherVersion, true, token);
            if (!token.IsCancellationRequested && store.Settings.LauncherAutoUpdate
                                               && generation == _launcherUpdateCheckGeneration && check.Matches(store.Settings))
            {
                _pendingLauncherUpdate = check.Release == null ? null : check;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            LogError(error);
        }
    }

    private void OfferPendingLauncherUpdate()
    {
        if (!store.Settings.LauncherAutoUpdate)
        {
            _pendingLauncherUpdate = null;
            return;
        }
        if (_pendingLauncherUpdate is not { Release: {} release } check
            || Busy || Modal.IsOpen || _showInstallationPrompt)
        {
            return;
        }
        _pendingLauncherUpdate = null;
        if (!check.Matches(store.Settings)
            || LauncherUpdateCheckService.IsDismissed(store.Settings, check.Repository, release.Id))
        {
            return;
        }
        Start(token => OfferLauncherUpdateAsync(check, token), "launcher.updates.checking");
    }

    private void SetLauncherAutoUpdate(bool enabled)
    {
        store.Settings.LauncherAutoUpdate = enabled;
        store.Save();
        ++_launcherUpdateCheckGeneration;
        _pendingLauncherUpdate = null;
        if (enabled)
        {
            _ = CheckLauncherInBackgroundAsync();
        }
    }

    private async Task OfferLauncherUpdateAsync(LauncherUpdateCheck check, CancellationToken token)
    {
        if (!check.Matches(store.Settings))
        {
            return;
        }
        string current = LauncherVersion;
        bool switchToStable = !store.Settings.LauncherPrereleases && current.Split('+')[0].Contains('-');
        EditorRelease? release = check.Release;
        if (release == null)
        {
            Notify(
                switchToStable ? "launcher.updates.no_stable_release" : "launcher.updates.launcher_up_to_date",
                switchToStable ? "" : "launcher.updates.no_updates",
                ToastType.Success
            );
            return;
        }

        string version = LauncherUpdaterService.VersionFor(LauncherUpdaterService.AssetFor(release, Platform.Identifier)!, Platform.Identifier)!;
        if (!await ConfirmAsync(
                switchToStable ? "launcher.updates.stable_title" : "launcher.updates.confirm_title",
                Loc.Get(switchToStable ? "launcher.updates.stable_description" : "launcher.updates.confirm_description", new
                {
                    version
                }),
                "launcher.updates.restart",
                token,
                changelog: release
            ))
        {
            token.ThrowIfCancellationRequested();
            LauncherUpdateCheckService.Dismiss(store, check.Repository, release.Id);
            return;
        }

        _operationTitle = Loc.Get("launcher.updates.installing", new
        {
            version
        });
        _immediateProgress = true;
        string executable = await new LauncherUpdaterService(_http, store).InstallAsync(release, Platform.Identifier, Transfer(), token);
        await LauncherStartupService.StartAsync(executable, store.Home, [], token);
        store.Settings.LauncherExecutable = executable;
        store.Save();
        _closeAfterCancel = true;
    }

    private async Task SaveRepositoriesAsync(CancellationToken token)
    {
        string prowl = GitHubRepositoryHelper.Normalize(_prowlRepositoryDraft ?? store.Settings.ProwlRepository);
        string launcher = GitHubRepositoryHelper.Normalize(_launcherRepositoryDraft ?? store.Settings.LauncherRepository);
        _backgroundCancellation.Cancel();

        store.Settings.ProwlRepository = prowl;
        store.Settings.LauncherRepository = launcher;
        _prowlRepositoryDraft = prowl;
        _launcherRepositoryDraft = launcher;
        store.Save();
        _releases = [];

        Notify("launcher.settings.repository_saved", prowl);
        await RefreshAsync(token, false);
    }

    private Task ResetRepositoryDraftsAsync(CancellationToken token)
    {
        _prowlRepositoryDraft = Constants.Defaults.ProwlRepository;
        _launcherRepositoryDraft = Constants.Defaults.LauncherRepository;
        return Task.CompletedTask;
    }
}
