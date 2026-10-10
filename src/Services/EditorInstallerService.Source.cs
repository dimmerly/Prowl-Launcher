using System.Diagnostics;
using System.Text.RegularExpressions;

using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class EditorInstallerService
{
    internal sealed record SourceBuildResult(InstalledEditor Editor, bool AlreadyInstalled);

    internal async Task<SourceBuildResult> InstallMainAsync(
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default,
        string? sourceRepository = null,
        Func<ProcessStartInfo, CancellationToken, Task<string>>? runCommand = null)
    {
        string repository = GitHubRepositoryHelper.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        InstalledEditor editor = new(
            0,
            "main",
            platform,
            platform.StartsWith("win-", StringComparison.Ordinal) ? "Prowl.Editor.exe" : "Prowl.Editor",
            DateTimeOffset.UtcNow)
        {
            Repository = repository
        };
        // Validate the platform before doing any source work.
        store.InstallPath(editor);
        using FileStream operation = AcquireOperationLock();
        RecoverInterruptedInstalls();

        Directory.CreateDirectory(store.WorkPath);
        Directory.CreateDirectory(store.VersionsPath);
        string sources = Path.Combine(store.Home, "Source");
        Directory.CreateDirectory(sources);
        foreach (string path in new[] { store.Home, store.WorkPath, store.VersionsPath, sources })
        {
            RejectLinks(path);
        }
        string checkout = Path.Combine(sources, GitHubRepositoryHelper.CacheKey(repository));
        string work = Path.Combine(store.WorkPath, Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "extracted");
        string backup = Path.Combine(work, "previous");
        Directory.CreateDirectory(work);

        async Task<string> RunAsync(string executable, string directory, params string[] arguments)
        {
            ProcessStartInfo info = new(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            // Avoid interactive Git prompts in the launcher's background operation.
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            return runCommand != null
                ? await runCommand(info, token)
                : await RunSourceCommandAsync(info, progress, token);
        }

        try
        {
            if (!Directory.Exists(checkout))
            {
                progress?.Report(new("launcher.versions.main_cloning"));
                string cloned = Path.Combine(work, "source");
                await RunAsync("git", work, "clone", "--branch", "main", "--single-branch",
                    "https://github.com/" + repository + ".git", cloned);
                Directory.Move(cloned, checkout);
            }
            else
            {
                RejectLinks(checkout);
                string changes = await RunAsync("git", checkout, "status", "--porcelain");
                if (!string.IsNullOrWhiteSpace(changes))
                {
                    throw new IOException(Loc.Get("launcher.versions.main_dirty"));
                }

                await RunAsync("git", checkout, "switch", "main");
                progress?.Report(new("launcher.versions.main_pulling"));
                await RunAsync("git", checkout, "pull", "--ff-only", "origin", "main");
            }
            string commit = (await RunAsync("git", checkout, "rev-parse", "HEAD")).Trim();
            if (!Regex.IsMatch(commit, @"\A[0-9a-f]{40}\z"))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.invalid_installation"));
            }
            editor = editor with { Tag = "main-" + commit[..7], SourceCommit = commit };
            string destination = store.InstallPath(editor);
            InstalledEditor? installed = store.InstalledEditors().FirstOrDefault(candidate =>
                candidate.IsMain && candidate.SourceCommit == commit && candidate.Platform == platform
                && candidate.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase)
                && File.Exists(store.ExecutablePath(candidate))
                && File.Exists(Path.Combine(store.InstallPath(candidate), "Prowl.Editor.dll"))
                && File.Exists(Path.Combine(store.InstallPath(candidate), "Prowl.Editor.runtimeconfig.json")));
            if (installed != null)
            {
                progress?.Report(new("launcher.versions.main_current", 1));
                return new SourceBuildResult(installed, AlreadyInstalled: true);
            }

            await RunAsync("git", checkout, "submodule", "sync", "--recursive");
            await RunAsync("git", checkout, "submodule", "update", "--init", "--recursive");

            progress?.Report(new("launcher.versions.main_building"));
            await RunAsync("dotnet", checkout, "publish", "Prowl.Editor/Prowl.Editor.csproj",
                "-c", "Release", "-r", platform, "--self-contained", "false", "-o", staging);
            if (!File.Exists(Path.Combine(staging, editor.ExecutableRelativePath)))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.missing_editor_archive"));
            }

            editor = editor with
            {
                EditorVersion = FileVersionInfo.GetVersionInfo(Path.Combine(staging, "Prowl.Editor.dll")).ProductVersion
            };
            LauncherStore.WriteJson(Path.Combine(staging, "installation.json"), editor);
            token.ThrowIfCancellationRequested();
            if (IsRunning(editor))
            {
                throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_repair"));
            }

            // Use the same repair journal as release installs so an interrupted replacement can recover.
            LauncherStore.WriteJson(Path.Combine(work, "repair.json"), editor);
            if (Directory.Exists(destination))
            {
                RejectLinks(destination);
                Directory.Move(destination, backup);
            }
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, destination);
                }
                throw;
            }
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }
            if (store.Settings.DefaultEditorKey == null)
            {
                store.Settings.DefaultEditorKey = editor.Key;
                store.Save();
            }
            progress?.Report(new("launcher.versions.main_ready", 1));
            return new SourceBuildResult(editor, AlreadyInstalled: false);
        }
        finally
        {
            if (Directory.Exists(work) && !Directory.Exists(backup))
            {
                Directory.Delete(work, true);
            }
        }
    }

    private static async Task<string> RunSourceCommandAsync(
        ProcessStartInfo info,
        IProgress<TransferProgress>? progress,
        CancellationToken token)
    {
        using Process process = new()
        {
            StartInfo = info
        };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new IOException(Loc.Get("launcher.versions.main_tool_missing", new
            {
                tool = info.FileName
            }), error);
        }
        List<string> output = [];
        List<string> errors = [];
        async Task Drain(StreamReader reader, List<string> lines)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                lines.Add(line);
                if (lines.Count > 30)
                {
                    lines.RemoveAt(0);
                }
                progress?.Report(new(line));
            }
        }
        Task stdout = Drain(process.StandardOutput, output);
        Task stderr = Drain(process.StandardError, errors);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr);
            if (process.ExitCode != 0)
            {
                throw new IOException($"{info.FileName} exited with code {process.ExitCode}.\n" + string.Join('\n', output.Concat(errors)));
            }
            return string.Join('\n', output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
