using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class EditorInstallerService
{
    /// <summary>
    ///     Clones or resets the repository's main branch into Source/, then publishes and installs the
    ///     editor for that commit unless it is already installed.
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

        Task<string> Git(string directory, params string[] arguments) => RunAsync("git", directory, arguments);

        Task<string> RunAsync(string executable, string directory, params string[] arguments)
        {
            ProcessStartInfo info = ProcessHelper.Captured(executable, directory, arguments);
            // Avoid interactive Git prompts in the launcher's background operation.
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            return runCommand != null ? runCommand(info, token) : RunSourceCommandAsync(info, progress, token);
        }

        if (!Directory.Exists(checkout))
        {
            progress?.Report(new TransferProgress("launcher.versions.main_cloning"));
            string cloned = Path.Combine(workspace.Work, "source");
            await Git(workspace.Work, "clone", "--branch", "main", "--single-branch",
                Constants.Network.GitHubUrl + repository + ".git", cloned);
            Directory.Move(cloned, checkout);
        }
        else
        {
            RejectLink(checkout);
            progress?.Report(new TransferProgress("launcher.versions.main_pulling"));
            await Git(checkout, "fetch", "origin", "main");
            // The checkout is launcher-owned; discard anything a botched install left behind.
            await Git(checkout, "checkout", "--force", "-B", "main", "origin/main");
            await Git(checkout, "clean", "-fd");
        }

        string commit = (await Git(checkout, "rev-parse", "HEAD")).Trim();
        if (!Regex.IsMatch(commit, @"\A[0-9a-f]{40}\z"))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_installation"));
        }

        editor = editor with { Tag = "main-" + commit[..7], SourceCommit = commit };

        if (FindInstalledBuild(commit, platform, repository) is { } installed)
        {
            progress?.Report(new TransferProgress("launcher.versions.main_current", 1));
            return new SourceBuildResult(installed, true);
        }

        await Git(checkout, "submodule", "sync", "--recursive");
        await Git(checkout, "submodule", "update", "--init", "--recursive", "--force");
        await Git(checkout, "submodule", "foreach", "--recursive", "git", "clean", "-fd");

        progress?.Report(new TransferProgress("launcher.versions.main_building"));
        await RunAsync("dotnet", checkout, "publish", "Prowl.Editor/Prowl.Editor.csproj",
            "-c", "Release", "-r", platform, "--self-contained", "false", "-o", workspace.Staging);
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

    /// <summary>A complete existing installation built from <paramref name="commit" />, if any.</summary>
    private InstalledEditor? FindInstalledBuild(string commit, string platform, string repository) =>
        store.InstalledEditors().FirstOrDefault(candidate =>
            candidate.IsMain
            && candidate.SourceCommit == commit
            && candidate.Platform == platform
            && candidate.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)
            && File.Exists(store.ExecutablePath(candidate))
            && File.Exists(Path.Combine(store.InstallPath(candidate), EditorName + ".dll"))
            && File.Exists(Path.Combine(store.InstallPath(candidate), EditorName + ".runtimeconfig.json")));

    /// <summary>Runs a build tool, streaming its output as progress. Returns stdout; throws with the output tail on failure.</summary>
    private static async Task<string> RunSourceCommandAsync(
        ProcessStartInfo info,
        IProgress<TransferProgress>? progress,
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

                progress?.Report(new TransferProgress(line));
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
