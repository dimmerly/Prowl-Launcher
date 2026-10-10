using Prowl.Rosetta;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

/// <summary>Install launcher updates side by side; the original entry point forwards to the chosen version.</summary>
public sealed class LauncherUpdaterService(HttpClient http, LauncherStore store)
{
    private sealed record Installation(string Digest, string Executable, Dictionary<string, string> Files);
    public static string UpdatesPath(LauncherStore store) => Path.Combine(store.Home, "Updates");
    internal static FileStream LockUpdates(LauncherStore store) => new(Path.Combine(store.Home, "launcher-update.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public static bool IsNewer(string version, string currentVersion)
    {
        if (!Regex.IsMatch(version, $"^{Constants.Startup.VersionPattern}$") || !Regex.IsMatch(currentVersion, $"^{Constants.Startup.VersionPattern}$"))
        {
            return false;
        }

        string[] candidate = version.Split('+')[0].Split('-', 2);
        string[] current = currentVersion.Split('+')[0].Split('-', 2);
        if (!Version.TryParse(candidate[0], out Version? next) || !Version.TryParse(current[0], out Version? installed))
        {
            return false;
        }

        int comparison = next.CompareTo(installed);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        if (candidate.Length == 1 || current.Length == 1)
        {
            return candidate.Length < current.Length;
        }

        string[] nextPreview = candidate[1].Split('.'), currentPreview = current[1].Split('.');
        for (int i = 0; i < Math.Min(nextPreview.Length, currentPreview.Length); i++)
        {
            bool nextNumeric = long.TryParse(nextPreview[i], out long nextNumber);
            bool currentNumeric = long.TryParse(currentPreview[i], out long currentNumber);
            comparison = nextNumeric && currentNumeric ? nextNumber.CompareTo(currentNumber) : nextNumeric != currentNumeric ? nextNumeric ? -1 : 1 : string.CompareOrdinal(nextPreview[i], currentPreview[i]);
            if (comparison != 0)
            {
                return comparison > 0;
            }
        }

        return nextPreview.Length > currentPreview.Length;
    }

    public static string? VersionFor(ReleaseAsset asset, string platform)
    {
        Match match = Regex.Match(asset.Name, $"^Prowl[- .]Launcher-({Constants.Startup.VersionPattern})-{Regex.Escape(platform)}\\.zip$");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static ReleaseAsset? AssetFor(EditorRelease release, string platform) => release.Assets
        .FirstOrDefault(asset => VersionFor(asset, platform) != null);

    public static EditorRelease? FindUpdate(IEnumerable<EditorRelease> releases, string platform, string currentVersion, bool includePrereleases = false)
    {
        EditorRelease? newest = null;
        string? newestVersion = !includePrereleases && currentVersion.Split('+')[0].Contains('-') ? null : currentVersion;
        foreach (EditorRelease release in releases.Where(release => !release.Draft && (includePrereleases || !release.Preview)))
        {
            ReleaseAsset? asset = AssetFor(release, platform);
            string? version = asset == null ? null : VersionFor(asset, platform);
            if (version == null || !includePrereleases && version.Split('+')[0].Contains('-')
                                || newestVersion != null && !IsNewer(version, newestVersion))
            {
                continue;
            }

            newest = release;
            newestVersion = version;
        }
        return newest;
    }

    public async Task<string> InstallAsync(
        EditorRelease release,
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default
    )
    {
        if (release.Draft || release.Preview && !store.Settings.LauncherPrereleases
                          || release.Id <= 0 || !Constants.Storage.SupportedPlatforms.Contains(platform))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));
        }

        ReleaseAsset asset = AssetFor(release, platform) ?? throw new InvalidDataException(Loc.Get("launcher.errors.no_platform_update"));
        string repository = GitHubRepositoryHelper.Normalize(store.Settings.LauncherRepository);
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme != "https" || source.Host != "github.com"
            || !source.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        }

        string version = VersionFor(asset, platform)!;
        if (!store.Settings.LauncherPrereleases && version.Split('+')[0].Contains('-'))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));
        }

        string versions = Path.Combine(UpdatesPath(store), GitHubRepositoryHelper.CacheKey(repository));
        string target = LauncherStore.SafeChildPath(versions, $"{version}-{platform}");
        string name = platform.StartsWith("win-", StringComparison.Ordinal) ? "Prowl.Launcher.exe" : "Prowl.Launcher";
        Directory.CreateDirectory(versions);
        DirectoryReplacementService.RejectLink(store.Home);
        DirectoryReplacementService.RejectLink(UpdatesPath(store));
        DirectoryReplacementService.RejectLink(versions);
        using FileStream operation = new(
            Path.Combine(store.Home, "operations.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        string? executable = await Task.Run(() => ValidateInstallation(target, asset.Digest, name, token), token);
        if (executable == null && IsRunning(target))
        {
            throw new IOException("Close the launcher using this version before repairing its update.");
        }
        await Task.Run(() => DirectoryReplacementService.Recover(target,
            path => ValidateInstallation(path, null, name, token) != null), token);
        executable = await Task.Run(() => ValidateInstallation(target, asset.Digest, name, token), token);
        if (executable == null)
        {
            string work = Path.Combine(store.Home, "Work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            DirectoryReplacementService.RejectLink(store.WorkPath);
            try
            {
                string archive = Path.Combine(work, "launcher.zip");
                await PackageDownloadService.DownloadVerifiedAsync(http, asset, archive, progress, token);
                string extracted = Path.Combine(work, "extracted");
                await Task.Run(() => EditorInstallerService.ExtractArchive(archive, extracted, token), token);
                string? entry = Directory.EnumerateFiles(extracted, name, SearchOption.AllDirectories).FirstOrDefault();
                if (entry == null)
                {
                    throw new InvalidDataException(Loc.Get("launcher.errors.missing_launcher"));
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(entry, File.GetUnixFileMode(entry) | UnixFileMode.UserExecute);
                }

                await Task.Run(() => WriteInstallation(extracted, entry, asset.Digest!, token), token);
                token.ThrowIfCancellationRequested();
                if (IsRunning(target))
                {
                    throw new IOException("Close the launcher using this version before repairing its update.");
                }
                DirectoryReplacementService.Replace(extracted, target);
                executable = LauncherStore.SafeChildPath(target, Path.GetRelativePath(extracted, entry));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        return executable;
    }

    internal static void Cleanup(LauncherStore store)
    {
        // Protect the complete download/start/save handoff, including gaps between those steps.
        using FileStream update = LockUpdates(store);
        using FileStream operation = new(Path.Combine(store.Home, "operations.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Settings saved = LauncherStore.ReadJson<Settings>(Path.Combine(store.Home, "settings.json")) ?? store.Settings;
        DirectoryReplacementService.RejectLink(store.Home);
        // Also remove unused copies from the former storage directory.
        foreach (string root in new[] { UpdatesPath(store), Path.Combine(store.Home, "LauncherVersions") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            DirectoryReplacementService.RejectLink(root);
            foreach (string repository in Directory.EnumerateDirectories(root))
            {
                DirectoryReplacementService.RejectLink(repository);
                foreach (string version in Directory.EnumerateDirectories(repository))
                {
                    string target = LauncherStore.SafeChildPath(repository, Path.GetFileName(version));
                    if (Contains(target, saved.LauncherExecutable) || Contains(target, saved.InstalledLauncherExecutable)
                        || Contains(target, Environment.ProcessPath) || IsRunning(target))
                    {
                        continue;
                    }
                    DirectoryReplacementService.RejectLinks(target);
                    Directory.Delete(target, true);
                }
                if (!Directory.EnumerateFileSystemEntries(repository).Any())
                {
                    Directory.Delete(repository);
                }
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }
    }

    private static bool Contains(string root, string? executable) => executable != null
        && Path.GetFullPath(executable).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void WriteInstallation(string root, string executable, string digest, CancellationToken token)
    {
        Dictionary<string, string> files = [];
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetRelativePath(root, path) == Constants.Storage.LauncherManifest)
            {
                continue;
            }
            using FileStream input = File.OpenRead(path);
            files.Add(Path.GetRelativePath(root, path), Convert.ToHexString(SHA256.HashData(input)));
        }
        LauncherStore.WriteJson(Path.Combine(root, Constants.Storage.LauncherManifest), new Installation(digest, Path.GetRelativePath(root, executable), files));
    }

    private static string? ValidateInstallation(string root, string? digest, string name, CancellationToken token)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }
        try
        {
            DirectoryReplacementService.RejectLinks(root);
            Installation? installed = LauncherStore.ReadJson<Installation>(Path.Combine(root, Constants.Storage.LauncherManifest));
            if (installed?.Files == null || installed.Files.Count == 0
                                         || digest != null && !string.Equals(digest, installed.Digest, StringComparison.OrdinalIgnoreCase)
                                         || !installed.Files.ContainsKey(installed.Executable)
                                         || Path.GetFileName(installed.Executable) != name)
            {
                return null;
            }
            foreach ((string relative, string hash) in installed.Files)
            {
                token.ThrowIfCancellationRequested();
                using FileStream input = File.OpenRead(LauncherStore.SafeChildPath(root, relative));
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            return LauncherStore.SafeChildPath(root, installed.Executable);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsRunning(string target)
    {
        string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcessesByName("Prowl.Launcher"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is {} file && file.StartsWith(root,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
