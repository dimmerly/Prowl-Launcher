using System.Diagnostics;

using Xunit;

namespace Prowl.Launcher.Test.E2E;

[CollectionDefinition("E2E", DisableParallelization = true)]
public sealed class E2ECollection;

[Collection("E2E")]
[Trait("Category", "E2E")]
public sealed class UserWorkflowTests
{
    public static IEnumerable<object[]> Scenarios() => new[]
    {
        "EmptyProjects",
        "WindowIcon",
        "DismissToasts",
        "ToastsAboveProgress",
        "NavigatePages",
        "ReadLocalNews",
        "CancelNewProject",
        "CreateProjectAndOpenIt",
        "RejectInvalidProjectName",
        "RejectExistingProjectFolder",
        "OpenPinnedProject",
        "RemoveProject",
        "SearchProjects",
        "FavoriteProject",
        "ChooseProjectEditor",
        "MissingProject",
        "CancelFolderPicker",
        "AddExistingProject",
        "DuplicateProjectImport",
        "InstallEditor",
        "RepairEditor",
        "CancelUninstall",
        "ConfirmUninstall",
        "CorruptDownload",
        "CancelDownload",
        "CloseDuringDownload",
        "ManualUpdateChangelog",
        "ManualReoffersDismissedUpdate",
        "EnablePrereleases",
        "AcceptHealthyUpdate",
        "FailedUpdateStartup",
        "CloseOnEditorLaunch",
        "EditorConsoleLoading",
        "EditorConsoleFailure",
        "SampleDownloadOnce",
        "SampleUpdateAvailable",
        "SampleAlreadyLatest",
        "EditorUpdateIndicator"
    }.Select(scenario => new object[]
    {
        scenario
    });

    [Theory]
    [MemberData(nameof( Scenarios ))]
    public async Task CommonUserActionCompletesThroughTheRenderedUi(string scenario)
    {
        string root = ArtifactDirectory(scenario);
        await RunScenario(scenario, root);
    }

    [Fact]
    public async Task DismissingAStartupUpdatePreventsTheSamePromptAfterRelaunch()
    {
        string root = ArtifactDirectory("StartupDismissal");
        await RunScenario("StartupDismissFirst", root);
        await RunScenario("StartupDismissSecond", root);
    }

    [Fact]
    public async Task SettingsSwitchesRetainTheirStateAfterRelaunch()
    {
        string root = ArtifactDirectory("SettingsPersistence");
        await RunScenario("SettingsPersist", root);
        await RunScenario("SettingsAfterRestart", root);
    }

    private static string ArtifactDirectory(string scenario)
    {
        string artifacts = Environment.GetEnvironmentVariable("PROWL_E2E_ARTIFACTS")
                           ?? Path.Combine(Path.GetTempPath(), "ProwlLauncherE2E");
        string root = Path.Combine(artifacts, scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task RunScenario(string scenario, string root)
    {
        ProcessStartInfo info = new( "dotnet" )
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof( UserWorkflowTests ).Assembly.Location);
        info.ArgumentList.Add("--e2e");
        info.ArgumentList.Add(scenario);
        info.ArgumentList.Add(root);
        info.Environment["PROWL_LAUNCHER_HOME"] = Path.Combine(root, "home");
        info.Environment["PROWL_E2E_UPDATE_FAIL"] = scenario == "FailedUpdateStartup" ? "1" : "0";
        info.Environment["PROWL_E2E_EDITOR_FAIL"] = scenario == "EditorConsoleFailure" ? "1" : "0";
        info.Environment["PROWL_E2E_EDITOR_SLOW"] = scenario == "EditorConsoleLoading" ? "1" : "0";
        using Process child = Process.Start(info)!;
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds(60) );
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            // The healthy replacement remains alive and inherits the child's output handles.
            // Stop it before draining redirected output, otherwise ReadToEnd never completes.
            await StopUpdateProbe(root);
            string output = await stdout, errors = await stderr;
            await File.WriteAllTextAsync(Path.Combine(root, scenario + ".log"), output + "\n" + errors);
            Assert.True(child.ExitCode == 0, $"{scenario} failed (exit {child.ExitCode}). Artifacts: {root}\n{output}\n{errors}");
            Assert.Contains("PASS " + scenario, output);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            string output = await stdout, errors = await stderr;
            await File.WriteAllTextAsync(Path.Combine(root, scenario + ".log"), output + "\n" + errors);
            Assert.Fail($"{scenario} exceeded 60 seconds. Artifacts: {root}\n{output}\n{errors}");
        }
        finally
        {
            await StopUpdateProbe(root);
        }
    }

    private static async Task StopUpdateProbe(string root)
    {
        string probePid = Path.Combine(root, "home", "update-probe.pid");
        if (File.Exists(probePid) && int.TryParse(await File.ReadAllTextAsync(probePid), out int id))
        {
            try
            {
                using Process probe = Process.GetProcessById(id);
                if (!probe.HasExited)
                {
                    probe.Kill(entireProcessTree: true);
                }
                await probe.WaitForExitAsync();
            }
            catch (ArgumentException) {}
        }
    }

}
