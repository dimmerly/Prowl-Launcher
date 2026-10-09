using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private IReadOnlyList<EditorRelease> _releases = [];
    private IReadOnlyList<InstalledEditor> _installed = [];

    private EditorRelease? NewerEditorRelease() => FindEditorUpdate(_releases, _installed,
        store.Settings.ProwlRepository, Platform.Identifier, _includeEditorPrereleases);

    internal static EditorRelease? FindEditorUpdate(IEnumerable<EditorRelease> releases,
        IEnumerable<InstalledEditor> installed, string repository, string platform, bool includePrereleases)
    {
        string? current = null;
        foreach (InstalledEditor editor in installed.Where(editor => editor.Platform == platform
                                                                     && editor.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)))
        {
            string version = EditorVersion(editor.Tag);
            if (LauncherUpdaterService.IsNewer(version, "0.0.0")
                && (current == null || LauncherUpdaterService.IsNewer(version, current)))
            {
                current = version;
            }
        }
        if (current == null)
        {
            return null;
        }

        EditorRelease? update = null;
        foreach (EditorRelease release in releases.Where(release => !release.Draft
                                                                    && (includePrereleases || !release.Preview) && release.AssetFor(platform) != null))
        {
            string version = EditorVersion(release.Tag);
            if (!includePrereleases && version.Split('+')[0].Contains('-')
                || !LauncherUpdaterService.IsNewer(version, current))
            {
                continue;
            }
            update = release;
            current = version;
        }
        return update;
    }

    private static string EditorVersion(string tag)
    {
        string version = tag.TrimStart('v')
            .Replace("-preview-", "-preview.").Replace("-alpha-", "-alpha.")
            .Replace("-beta-", "-beta.").Replace("-rc-", "-rc.");
        // Historical editor tags used major.minor instead of major.minor.patch.
        int suffix = version.IndexOfAny(['-', '+']);
        string core = suffix < 0 ? version : version[..suffix];
        return core.Count(character => character == '.') == 1
            ? core + ".0" + (suffix < 0 ? "" : version[suffix..]) : version;
    }

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
