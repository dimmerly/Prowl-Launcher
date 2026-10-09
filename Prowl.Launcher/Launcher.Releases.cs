using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private IReadOnlyList<EditorRelease> _releases = [];
    private IReadOnlyList<InstalledEditor> _installed = [];

    private void ReloadInstalled() => _installed = store.InstalledEditors()
        .Where(e => e.Platform == Platform.Identifier)
        .ToArray();

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            // Startup refresh has no operation panel, alerts or effect on interactive controls.
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
        if (notify)
        {
            Notify(
                _github.UsedCache ? "launcher.network.offline" : "launcher.versions.releases_checked",
                _github.UsedCache ? "launcher.versions.cached_releases" : Loc.Get("launcher.versions.available_count", new
                {
                    count = _releases.Count(r => r.AssetFor(Platform.Identifier) != null), platform = Platform.Identifier
                }),
                _github.UsedCache ? ToastType.Warning : ToastType.Success
            );
        }
    }
}
