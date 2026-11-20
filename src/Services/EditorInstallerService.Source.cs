using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class EditorInstallerService
{
    /// <summary>
    ///     Clones or resets the repository's main branch into Source/ (and Anthology's, when building against it),
    ///     then publishes and installs the editor unless that build is already installed.
    /// </summary>
    internal async Task<SourceBuildResult> InstallMainAsync(
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default,
        string? sourceRepository = null,
        Func<ProcessStartInfo, CancellationToken, Task<string>>? runCommand = null)
    {
        string repository = GitHubRepository.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        InstalledEditor editor =
            new(0, "main", platform, Platform.ExecutableName(EditorName, platform), DateTimeOffset.UtcNow)
            {
                Repository = repository
            };
        // Validate the platform before doing any source work.
        store.InstallPath(editor);

        string sources = Path.Combine(store.Home, "Source");
        using FileStream operation = BeginOperation(sources);
        using InstallWorkspace workspace = new(store);
        string checkout = Path.Combine(sources, GitHubRepository.CacheKey(repository));

        // Git's output is labelled with the repository it's working on, since several are synced in turn.
        Task<string> Git(string remote, string directory, params string[] arguments) =>
            RunAsync("git", directory, remote + ": ", arguments);

        Task<string> RunAsync(string executable, string directory, string? label, params string[] arguments)
        {
            ProcessStartInfo info = ProcessHelper.Captured(executable, directory, arguments);
            // Avoid interactive Git prompts in the launcher's background operation.
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            // Abort clones and fetches whose transfer stalls instead of hanging on a dead connection.
            info.Environment["GIT_HTTP_LOW_SPEED_LIMIT"] = "1000";
            info.Environment["GIT_HTTP_LOW_SPEED_TIME"] =
                ((int)Constants.Network.DownloadIdleTimeout.TotalSeconds).ToString();
            return runCommand != null
                ? runCommand(info, token)
                : RunSourceCommandAsync(info, progress, label, token);
        }

        async Task<string> SyncMainAsync(string remote, string directory, string cloning, string pulling)
        {
            if (!Directory.Exists(directory))
            {
                progress?.Report(new TransferProgress(cloning));
                string cloned = Path.Combine(workspace.Work, Path.GetFileName(directory));
                await Git(remote, workspace.Work, "clone", "--progress", "--depth", "1", "--branch", "main",
                    Constants.Network.GitHubUrl + remote + ".git", cloned);
                Directory.Move(cloned, directory);
            }
            else
            {
                RejectLink(directory);
                progress?.Report(new TransferProgress(pulling));
                await Git(remote, directory, "fetch", "--progress", "--depth", "1", "origin", "main");
                // The checkout is launcher-owned; discard anything a botched install left behind.
                await Git(remote, directory, "checkout", "--force", "-B", "main", "origin/main");
                await Git(remote, directory, "clean", "-fd");
            }

            string head = (await Git(remote, directory, "rev-parse", "HEAD")).Trim();
            if (!Regex.IsMatch(head, @"\A[0-9a-f]{40}\z"))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.invalid_installation"));
            }

            return head;
        }

        async Task PrepareSubmodulesAsync(string remote, string directory)
        {
            await Git(remote, directory, "submodule", "sync", "--recursive");
            await Git(remote, directory, "submodule", "update", "--init", "--recursive", "--force", "--depth", "1");
            await Git(remote, directory, "submodule", "foreach", "--recursive", "git", "clean", "-fd");
        }

        string commit = await SyncMainAsync(repository, checkout,
            "launcher.versions.main_cloning", "launcher.versions.main_pulling");
        // Optionally build against Anthology's main branch directly instead of its published NuGet packages.
        // Anthology itself as the source repository would share its checkout, so it never gets one.
        string anthology = Path.Combine(sources, GitHubRepository.CacheKey(Constants.Defaults.AnthologyRepository));
        string? anthologyCommit =
            store.Settings.BuildWithLocalAnthology
            && !repository.Equals(Constants.Defaults.AnthologyRepository, StringComparison.OrdinalIgnoreCase)
                ? await SyncMainAsync(Constants.Defaults.AnthologyRepository, anthology,
                    "launcher.versions.anthology_cloning", "launcher.versions.anthology_pulling")
                : null;

        editor = editor with
        {
            Tag = "main-" + commit[..7], SourceCommit = commit, AnthologyCommit = anthologyCommit
        };

        if (FindInstalledBuild(commit, anthologyCommit, platform, repository) is { } installed)
        {
            progress?.Report(new TransferProgress("launcher.versions.main_current", 1));
            return new SourceBuildResult(installed, true);
        }

        // A rebuild of this commit (new Anthology, missing files) replaces its folder; fail before the long build.
        ThrowIfRunning(editor);
        await PrepareSubmodulesAsync(repository, checkout);
        List<string> publish =
        [
            "publish", "Prowl.Editor/Prowl.Editor.csproj", "-c", "Release", "-r", platform,
            "--self-contained", "false"
        ];
        if (anthologyCommit != null)
        {
            await PrepareSubmodulesAsync(Constants.Defaults.AnthologyRepository, anthology);
            publish.AddRange(["-p:ProwlLocalAnthology=true", "-p:AnthologyDir=" + anthology + Path.DirectorySeparatorChar]);
        }

        progress?.Report(new TransferProgress("launcher.versions.main_building"));
        await RunAsync("dotnet", checkout, null, [.. publish, "-o", workspace.Staging]);
        if (!File.Exists(Path.Combine(workspace.Staging, editor.ExecutableRelativePath)))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.missing_editor_archive"));
        }

        editor = editor with
        {
            EditorVersion = FileVersionInfo.GetVersionInfo(Path.Combine(workspace.Staging, EditorName + ".dll"))
                .ProductVersion
        };
        CommitInstall(editor, workspace, store.InstallPath(editor), token);
        progress?.Report(new TransferProgress("launcher.versions.main_ready", 1));
        return new SourceBuildResult(editor, false);
    }

    /// <summary>A complete existing installation built from <paramref name="commit" /> and <paramref name="anthologyCommit" />, if any.</summary>
    private InstalledEditor? FindInstalledBuild(string commit, string? anthologyCommit, string platform,
        string repository) =>
        store.InstalledEditors().FirstOrDefault(candidate =>
            candidate.IsMain
            && candidate.SourceCommit == commit
            && candidate.AnthologyCommit == anthologyCommit
            && candidate.Platform == platform
            && candidate.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)
            && File.Exists(store.ExecutablePath(candidate))
            && File.Exists(Path.Combine(store.InstallPath(candidate), EditorName + ".dll"))
            && File.Exists(Path.Combine(store.InstallPath(candidate), EditorName + ".runtimeconfig.json")));

    /// <summary>Runs a build tool, streaming its output (prefixed with <paramref name="label" />) as progress. Returns stdout; throws with the output tail on failure.</summary>
    private static async Task<string> RunSourceCommandAsync(
        ProcessStartInfo info,
        IProgress<TransferProgress>? progress,
        string? label,
        CancellationToken token)
    {
        const int MaxRetainedLines = 30;

        using Process process = new() { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception error)
        {
            throw new IOException(Loc.Get("launcher.versions.main_tool_missing", new { tool = info.FileName }), error);
        }

        List<string> output = [];
        List<string> errors = [];

        async Task Drain(StreamReader reader, List<string> lines)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                lines.Add(line);
                if (lines.Count > MaxRetainedLines)
                {
                    lines.RemoveAt(0);
                }

                progress?.Report(new TransferProgress(label + line));
            }
        }

        Task stdout = Drain(process.StandardOutput, output);
        Task stderr = Drain(process.StandardError, errors);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr);
            if (process.ExitCode != 0)
            {
                // Lead with the first compiler error so the toast's single line says what broke.
                string summary = output.Concat(errors).Select(FirstBuildError).FirstOrDefault(line => line != null)
                                 ?? $"{info.FileName} exited with code {process.ExitCode}.";
                throw new IOException(summary + "\n" + string.Join('\n', output.Concat(errors)));
            }

            return string.Join('\n', output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>An MSBuild error line as "File.cs(1,2): error CS0000: text", without its directory and project suffix.</summary>
    internal static string? FirstBuildError(string line)
    {
        Match match = Regex.Match(line, @"([^\\/]+\(\d+,\d+\): error \w+: .*?)(?: \[[^\]]+\])?\s*\z");
        return match.Success ? match.Groups[1].Value : null;
    }

    internal sealed record SourceBuildResult(InstalledEditor Editor, bool AlreadyInstalled);
}
