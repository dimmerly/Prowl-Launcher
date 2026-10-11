using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>The launcher's own updates: background checks, the update prompt and the restart handoff.</summary>
sealed class LauncherUpdateCoordinator(
    HttpClient http,
    LauncherStore store,
    OperationCoordinator operations,
    Dialogs dialogs,
    NotificationCenter notifications,
    Action<Exception> logError,
    bool allowBackgroundChecks
) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    // Bumped whenever settings change so an in-flight background check can't publish a stale result.
    private int _checkGeneration;
    private LauncherUpdateCheck? _pending;

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    /// <summary>Checks on demand, then offers the result (or reports there is nothing new).</summary>
    public async Task UpdateAsync(CancellationToken token)
    {
        InvalidatePending();
        LauncherUpdateCheck check = await new LauncherUpdaterService(http, store)
            .CheckAsync(Platform.Identifier, VersionHelper.LauncherVersion, false, token);
        await OfferAsync(check, token);
    }

    /// <summary>Checks quietly; a found update is offered later by <see cref="OfferPending" />.</summary>
    public async Task CheckInBackgroundAsync()
    {
        int generation = InvalidatePending();
        CancellationToken token = _cancellation.Token;
        if (!store.Settings.LauncherAutoUpdate || !allowBackgroundChecks)
        {
            return;
        }
        try
        {
            LauncherUpdateCheck check = await new LauncherUpdaterService(http, store)
                .CheckAsync(Platform.Identifier, VersionHelper.LauncherVersion, true, token);
            bool stillCurrent = !token.IsCancellationRequested
                                && store.Settings.LauncherAutoUpdate
                                && generation == _checkGeneration
                                && check.Matches(store.Settings);
            if (stillCurrent)
            {
                _pending = check.Release == null ? null : check;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            logError(error);
        }
    }

    /// <summary>
    ///     Called every frame; shows a pending update prompt once nothing else needs the user's attention.
    ///     <paramref name="userBusy" /> is true while a dialog or prompt is waiting on the user.
    /// </summary>
    public void OfferPending(bool userBusy)
    {
        if (!store.Settings.LauncherAutoUpdate)
        {
            _pending = null;
            return;
        }
        if (_pending is not { Release: {} release } check || operations.Busy || userBusy)
        {
            return;
        }
        _pending = null;
        if (!check.Matches(store.Settings) || LauncherUpdaterService.IsDismissed(store.Settings, check.Repository, release.Id))
        {
            return;
        }
        operations.Start(token => OfferAsync(check, token), "launcher.updates.checking");
    }

    public void SetAutoUpdate(bool enabled)
    {
        store.Settings.LauncherAutoUpdate = enabled;
        store.Save();
        InvalidatePending();
        if (enabled)
        {
            _ = CheckInBackgroundAsync();
        }
    }

    /// <summary>Leaving the prerelease channel while on a prerelease offers the newest stable version right away.</summary>
    public void SetPrereleases(bool enabled)
    {
        store.Settings.LauncherPrereleases = enabled;
        store.Save();
        if (enabled)
        {
            _ = CheckInBackgroundAsync();
        }
        else if (VersionHelper.IsPreview(VersionHelper.LauncherVersion))
        {
            operations.Start(UpdateAsync, "launcher.updates.checking");
        }
    }

    public void Cleanup()
    {
        try
        {
            LauncherUpdaterService.Cleanup(store);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Cleanup must not stop startup or a successfully verified update.
            logError(error);
        }
    }

    private int InvalidatePending()
    {
        _pending = null;
        return ++_checkGeneration;
    }

    /// <summary>
    ///     Asks to install <paramref name="check" />'s release. Declining dismisses that release. Accepting installs
    ///     it, starts it, and closes this launcher once the new one reports its window is ready.
    /// </summary>
    private async Task OfferAsync(LauncherUpdateCheck check, CancellationToken token)
    {
        if (!check.Matches(store.Settings))
        {
            return;
        }
        bool switchToStable = !store.Settings.LauncherPrereleases && VersionHelper.IsPreview(VersionHelper.LauncherVersion);
        if (check.Release is not {} release)
        {
            notifications.Notify(
                switchToStable ? "launcher.updates.no_stable_release" : "launcher.updates.launcher_up_to_date",
                switchToStable ? "" : "launcher.updates.no_updates");
            return;
        }

        string version = LauncherUpdaterService.VersionFor(LauncherUpdaterService.AssetFor(release, Platform.Identifier)!, Platform.Identifier)!;
        bool accepted = await dialogs.ConfirmAsync(
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

        operations.Title = Loc.Get("launcher.updates.installing", new
        {
            version
        });
        operations.ImmediateProgress = true;
        using (FileStream update = LauncherUpdaterService.LockUpdates(store))
        {
            string executable = await new LauncherUpdaterService(http, store).InstallAsync(release, Platform.Identifier, operations.Transfer(), token);
            await LauncherStartupService.StartAsync(executable, store.Home, [], token);
            store.Settings.LauncherExecutable = executable;
            store.Save();
        }
        operations.CloseWhenDone = true;
        Cleanup();
    }
}
