using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
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
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
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
        GitHubReleasesService github = new(_http, store, store.Settings.LauncherRepository);
        IReadOnlyList<EditorRelease> releases = await github.GetAsync(token);
        string current = LauncherVersion;
        bool switchToStable = !store.Settings.LauncherPrereleases && current.Split('+')[0].Contains('-');
        EditorRelease? release = LauncherUpdaterService.FindUpdate(releases, Platform.Identifier, current, store.Settings.LauncherPrereleases);
        if (release == null)
        {
            Notify(
                github.UsedCache ? "launcher.updates.check_failed"
                    : switchToStable ? "launcher.updates.no_stable_release" : "launcher.updates.launcher_up_to_date",
                github.UsedCache ? "launcher.updates.cached_releases"
                    : switchToStable ? "" : "launcher.updates.no_updates",
                github.UsedCache ? ToastType.Warning : ToastType.Success
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
                token
            ))
        {
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

        Notify("launcher.preferences.repository_saved", prowl);
        await RefreshAsync(token, false);
    }

    private Task ResetRepositoryDraftsAsync(CancellationToken token)
    {
        _prowlRepositoryDraft = GitHubRepositoryHelper.DefaultProwl;
        _launcherRepositoryDraft = GitHubRepositoryHelper.LauncherRepository;
        return Task.CompletedTask;
    }
}
