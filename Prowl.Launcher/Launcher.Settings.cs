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
        GitHubReleasesService github = new(_http, store, GitHubRepositoryHelper.LauncherRepository);
        IReadOnlyList<EditorRelease> releases = await github.GetAsync(token);
        string current = LauncherVersion;
        EditorRelease? release = LauncherUpdaterService.FindUpdate(releases, Platform.Identifier, current);
        if (release == null)
        {
            Notify(
                github.UsedCache ? "launcher.updates.check_failed" : "launcher.updates.launcher_up_to_date",
                github.UsedCache ? "launcher.updates.cached_releases" : "launcher.updates.no_updates",
                github.UsedCache ? ToastType.Warning : ToastType.Success
            );
            return;
        }

        string version = LauncherUpdaterService.VersionFor(LauncherUpdaterService.AssetFor(release, Platform.Identifier)!, Platform.Identifier)!;
        if (!await ConfirmAsync(
                "launcher.updates.confirm_title",
                Loc.Get("launcher.updates.confirm_description", new
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
        _restart = await new LauncherUpdaterService(_http, store).InstallAsync(release, Platform.Identifier, Transfer(), token);
    }

    private async Task SaveRepositoriesAsync(CancellationToken token)
    {
        string prowl = GitHubRepositoryHelper.Normalize(_prowlRepositoryDraft ?? store.Settings.ProwlRepository);
        _backgroundCancellation.Cancel();

        store.Settings.ProwlRepository = prowl;
        _prowlRepositoryDraft = prowl;
        store.Save();
        _releases = [];

        Notify("launcher.preferences.repository_saved", prowl);
        await RefreshAsync(token, false);
    }

    private Task ResetRepositoryDraftsAsync(CancellationToken token)
    {
        _prowlRepositoryDraft = GitHubRepositoryHelper.DefaultProwl;
        return Task.CompletedTask;
    }
}
