using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>The launcher's own updates: background checks, the update prompt and the restart handoff.</summary>
public sealed partial class Launcher
{
    // Bumped whenever settings change so an in-flight background check can't publish a stale result.
    private int _launcherUpdateCheckGeneration;
    private LauncherUpdateCheck? _pendingLauncherUpdate;

    /// <summary>Checks on demand, then offers the result (or reports there is nothing new).</summary>
    private async Task UpdateLauncherAsync(CancellationToken token)
    {
        InvalidatePendingLauncherUpdate();
        LauncherUpdateCheck check = await new LauncherUpdaterService(_http, store)
            .CheckAsync(Platform.Identifier, VersionHelper.LauncherVersion, false, token);
        await OfferLauncherUpdateAsync(check, token);
    }

    /// <summary>Checks quietly; a found update is offered later by <see cref="OfferPendingLauncherUpdate" />.</summary>
    private async Task CheckLauncherInBackgroundAsync()
    {
        int generation = InvalidatePendingLauncherUpdate();
        CancellationToken token = _launcherUpdateCancellation.Token;
        if (!store.Settings.LauncherAutoUpdate || offline || screenshot != null)
        {
            return;
        }
        try
        {
            LauncherUpdateCheck check = await new LauncherUpdaterService(_http, store)
                .CheckAsync(Platform.Identifier, VersionHelper.LauncherVersion, true, token);
            bool stillCurrent = !token.IsCancellationRequested
                                && store.Settings.LauncherAutoUpdate
                                && generation == _launcherUpdateCheckGeneration
                                && check.Matches(store.Settings);
            if (stillCurrent)
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

    /// <summary>Called every frame; shows a pending update prompt once nothing else needs the user's attention.</summary>
    private void OfferPendingLauncherUpdate()
    {
        if (!store.Settings.LauncherAutoUpdate)
        {
            _pendingLauncherUpdate = null;
            return;
        }
        if (_pendingLauncherUpdate is not { Release: {} release } check || Busy || Modal.IsOpen || _showInstallationPrompt)
        {
            return;
        }
        _pendingLauncherUpdate = null;
        if (!check.Matches(store.Settings) || LauncherUpdaterService.IsDismissed(store.Settings, check.Repository, release.Id))
        {
            return;
        }
        Start(token => OfferLauncherUpdateAsync(check, token), "launcher.updates.checking");
    }

    private void SetLauncherAutoUpdate(bool enabled)
    {
        store.Settings.LauncherAutoUpdate = enabled;
        store.Save();
        InvalidatePendingLauncherUpdate();
        if (enabled)
        {
            _ = CheckLauncherInBackgroundAsync();
        }
    }

    /// <summary>Leaving the prerelease channel while on a prerelease offers the newest stable version right away.</summary>
    private void SetLauncherPrereleases(bool enabled)
    {
        store.Settings.LauncherPrereleases = enabled;
        store.Save();
        if (enabled)
        {
            _ = CheckLauncherInBackgroundAsync();
        }
        else if (VersionHelper.IsPreview(VersionHelper.LauncherVersion))
        {
            Start(UpdateLauncherAsync, "launcher.updates.checking");
        }
    }

    private int InvalidatePendingLauncherUpdate()
    {
        _pendingLauncherUpdate = null;
        return ++_launcherUpdateCheckGeneration;
    }

    /// <summary>
    ///     Asks to install <paramref name="check" />'s release. Declining dismisses that release. Accepting installs
    ///     it, starts it, and closes this launcher once the new one reports its window is ready.
    /// </summary>
    private async Task OfferLauncherUpdateAsync(LauncherUpdateCheck check, CancellationToken token)
    {
        if (!check.Matches(store.Settings))
        {
            return;
        }
        bool switchToStable = !store.Settings.LauncherPrereleases && VersionHelper.IsPreview(VersionHelper.LauncherVersion);
        if (check.Release is not {} release)
        {
            Notify(
                switchToStable ? "launcher.updates.no_stable_release" : "launcher.updates.launcher_up_to_date",
                switchToStable ? "" : "launcher.updates.no_updates");
            return;
        }

        string version = LauncherUpdaterService.VersionFor(LauncherUpdaterService.AssetFor(release, Platform.Identifier)!, Platform.Identifier)!;
        bool accepted = await ConfirmAsync(
            switchToStable ? "launcher.updates.stable_title" : "launcher.updates.confirm_title",
            Loc.Get(switchToStable ? "launcher.updates.stable_description" : "launcher.updates.confirm_description", new
            {
                version
            }),
            "launcher.updates.restart",
            token,
            changelog: release);
        if (!accepted)
        {
            token.ThrowIfCancellationRequested();
            LauncherUpdaterService.Dismiss(store, check.Repository, release.Id);
            return;
        }

        _operationTitle = Loc.Get("launcher.updates.installing", new
        {
            version
        });
        _immediateProgress = true;
        using (FileStream update = LauncherUpdaterService.LockUpdates(store))
        {
            string executable = await new LauncherUpdaterService(_http, store).InstallAsync(release, Platform.Identifier, Transfer(), token);
            await LauncherStartupService.StartAsync(executable, store.Home, [], token);
            store.Settings.LauncherExecutable = executable;
            store.Save();
        }
        _closeAfterCancel = true;
        CleanupLauncherUpdates();
    }

    private void CleanupLauncherUpdates()
    {
        try
        {
            LauncherUpdaterService.Cleanup(store);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Cleanup must not stop startup or a successfully verified update.
            LogError(error);
        }
    }
}
