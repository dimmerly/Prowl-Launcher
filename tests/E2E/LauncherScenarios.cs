using System.Text.Json;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using static Prowl.Launcher.Test.E2E.UiDriver;

namespace Prowl.Launcher.Test.E2E;

internal static class LauncherScenarios
{
    private static InstalledEditor? _editor;
    private static Project? _project;
    private static string? _secondEditorKey;

    internal static async Task Prepare(string scenario, LauncherFixture fixture)
    {
        if (scenario.StartsWith("Sample", StringComparison.Ordinal))
        {
            fixture.PublishSamples();
            LauncherUpdateCheckService.Dismiss(fixture.Store, fixture.Store.Settings.LauncherRepository, 201);
            if (scenario is "SampleUpdateAvailable" or "SampleAlreadyLatest")
                await new SampleService(fixture.Http, fixture.Store).EnsureDownloadedAsync();
            if (scenario == "SampleUpdateAvailable") fixture.PublishSamples(newer: true);
        }
        string[] installedScenarios = ["CreateProjectAndOpenIt", "RejectInvalidProjectName", "RejectExistingProjectFolder",
            "OpenPinnedProject", "ChooseProjectEditor", "RepairEditor", "CancelUninstall", "ConfirmUninstall",
            "CorruptDownload", "CloseOnEditorLaunch"];
        if (installedScenarios.Contains(scenario)) _editor = await fixture.InstallEditor();
        string[] projectScenarios = ["OpenPinnedProject", "RemoveProject", "FavoriteProject", "ChooseProjectEditor",
            "MissingProject", "ConfirmUninstall", "CloseOnEditorLaunch"];
        if (projectScenarios.Contains(scenario)) _project = fixture.AddProject("Alpha Game", _editor);
        if (scenario == "SearchProjects")
        {
            fixture.AddProject("Alpha Game");
            fixture.AddProject("Beta Game");
        }
        if (scenario == "FavoriteProject") fixture.AddProject("Beta Game");
        if (scenario is "AddExistingProject" or "DuplicateProjectImport")
        {
            _project = fixture.AddProject("Imported Game");
            fixture.Store.Settings.Projects.Clear();
            fixture.Store.Save();
        }
        if (scenario == "MissingProject") Directory.Delete(_project!.Path, true);
        if (scenario == "RepairEditor" || scenario == "CorruptDownload")
            File.WriteAllText(fixture.Store.ExecutablePath(_editor!), "broken editor");
        if (scenario == "RejectExistingProjectFolder")
        {
            Directory.CreateDirectory(Path.Combine(fixture.Projects, "My Game"));
            File.WriteAllText(Path.Combine(fixture.Projects, "My Game", "keep.txt"), "Existing data");
        }
        if (scenario == "ChooseProjectEditor")
        {
            EditorRelease release = fixture.EditorRelease with
            {
                Id = 12, Tag = "v1.1.0", Assets = [fixture.EditorRelease.Assets[0] with { Name = $"Prowl-v1.1.0-{Platform.Identifier}.zip" }]
            };
            _secondEditorKey = (await new EditorInstallerService(fixture.Http, fixture.Store).InstallAsync(release, Platform.Identifier)).Key;
        }
        if (scenario == "ManualReoffersDismissedUpdate")
            LauncherUpdateCheckService.Dismiss(fixture.Store, fixture.Store.Settings.LauncherRepository, 201);
        if (scenario == "CloseOnEditorLaunch")
        {
            fixture.Store.Settings.CloseOnEditorLaunch = true;
            fixture.Store.Save();
        }
    }

    internal static async Task Run(string scenario, LauncherFixture f, UiDriver ui)
    {
        await ui.Frame(3);
        switch (scenario)
        {
            case "SampleDownloadOnce":
                await ui.Navigate("Samples");
                f.DelaySampleDownload = true;
                await ui.ClickText("Hello Prowl");
                await ui.Wait(() => ui.HasText("Downloading samples") && ui.HasText("Cancel"), "Sample download should show progress at the top.");
                Check(ui.Text("Downloading samples").Data.Y < ui.Text("Hello Prowl").Data.Y, "Download progress must appear above sample cards.");
                await ui.Wait(() => File.Exists(Path.Combine(f.Home, "sample-launch.log")), "The selected sample should launch after the shared download.");
                await ui.Wait(() => UiDriver.GetField<object?>(ui.Launcher, "_operation") == null, "Sample startup should finish.");
                await ui.ClickText("Physics Showcase");
                await ui.Wait(() => File.ReadAllLines(Path.Combine(f.Home, "sample-launch.log")).Length == 2, "A second sample should use the shared local runtime.");
                Check(f.SampleDownloads == 1, "All sample cards must share one download.");
                Check(!ui.HasText("Update samples"), "The update button should stay hidden when the local bundle is latest.");
                break;
            case "SampleUpdateAvailable":
                await ui.Navigate("Samples");
                await ui.Wait(() => ui.HasText("Update samples"), "A newer remote bundle should expose the update button.");
                f.DelaySampleDownload = true;
                await ui.ClickText("Update samples");
                await ui.Wait(() => ui.HasText("Downloading samples"), "Updating samples should expose download progress.");
                await ui.Wait(() => ui.HasText("Samples updated") && !ui.HasText("Update samples"), "The update button should disappear after a successful refresh.");
                Check(f.SampleDownloads == 2, "Updating should download one replacement bundle.");
                break;
            case "SampleAlreadyLatest":
                await ui.Navigate("Samples");
                await ui.Wait(() => !UiDriver.GetField<bool>(ui.Launcher, "_sampleUpdateChecking"), "The background sample check should finish.");
                await ui.Frame(10);
                Check(!ui.HasText("Update samples"), "The update button must be hidden without a newer bundle.");
                Check(f.SampleDownloads == 1, "Checking for updates must not download anything.");
                break;
            case "EmptyProjects":
                Check(ui.HasText("Start something new"), "An empty launcher must explain how to start.");
                Check(ui.HasText("Add project") && ui.HasText("New project"), "Project actions must be available.");
                break;
            case "NavigatePages":
                await ui.Navigate("Versions");
                await ui.Wait(() => ui.HasText("Available from GitHub"), "Versions page should show its catalog.");
                await ui.Navigate("Samples");
                await ui.Wait(() => ui.HasText("Samples"), "Samples page should open.");
                await ui.Navigate("Settings");
                await ui.Wait(() => ui.HasText("Appearance"), "Preferences page should open.");
                await ui.Navigate("Projects");
                Check(ui.HasText("Start something new"), "Returning to Projects should preserve the empty state.");
                break;
            case "CancelNewProject":
                await ui.ClickText("New project…");
                await ui.Wait(() => ui.HasText("Name:"), "New project form should open.");
                await ui.ClickText("Cancel");
                Check(f.Saved().Projects.Count == 0 && !Directory.EnumerateDirectories(f.Projects).Any(), "Cancel must create no project files or records.");
                break;
            case "CreateProjectAndOpenIt":
            case "RejectInvalidProjectName":
            case "RejectExistingProjectFolder":
                await ui.ClickText("New project…");
                await ui.FillBelowLabel("Name:", scenario == "RejectInvalidProjectName" ? "../escape" : "My Game");
                await ui.FillBelowLabel("Path:", f.Projects);
                await ui.ClickText("Create & open");
                if (scenario == "CreateProjectAndOpenIt")
                {
                    await ui.Wait(() => File.Exists(f.LaunchRecord), "Creating a project should start the selected editor.");
                    Project created = f.Saved().Projects.Single();
                    Check(created.Name == "My Game" && created.EditorKey == _editor!.Key, "The new project must be saved with the selected editor.");
                    Check(created.LastOpened != default, "Launching must record the last-opened time.");
                    Check(File.Exists(Path.Combine(created.Path, "My Game.prowl")), "The editor project marker must exist.");
                    AssertLaunch(f, created.Path);
                }
                else
                {
                    string error = scenario == "RejectInvalidProjectName" ? "Choose a valid project folder name." : "A folder with this project name already exists.";
                    await ui.Wait(() => ui.HasText(error), "The form should explain why project creation failed.");
                    Check(f.Saved().Projects.Count == 0 && !File.Exists(f.LaunchRecord), "Invalid creation must not register or launch a project.");
                    if (scenario == "RejectExistingProjectFolder")
                        Check(File.ReadAllText(Path.Combine(f.Projects, "My Game", "keep.txt")) == "Existing data", "Existing project data must not be overwritten.");
                }
                break;
            case "OpenPinnedProject":
            case "CloseOnEditorLaunch":
                if (scenario == "CloseOnEditorLaunch") ui.ExpectClose();
                await ui.ClickText("Open");
                if (scenario == "CloseOnEditorLaunch")
                {
                    await ui.Wait(() => false, "Close-on-launch should close the window after starting the editor.", 15);
                    return;
                }
                await ui.Wait(() => File.Exists(f.LaunchRecord), "Opening a project should start its pinned editor.");
                AssertLaunch(f, _project!.Path);
                Check(f.Saved().Projects.Single().LastOpened != default, "Opening must persist its timestamp.");
                break;
            case "RemoveProject":
                await ui.ClickText("Remove");
                await ui.Wait(() => f.Saved().Projects.Count == 0, "Remove should forget the project.");
                Check(File.ReadAllText(Path.Combine(_project!.Path, "Assets", "keep.txt")) == "User project data", "Remove must preserve all project files.");
                break;
            case "SearchProjects":
                // Empty text fields render their placeholder as an ordinary text node.
                await ui.ReplaceText("Search projects…", "alpha");
                await ui.Wait(() => ui.HasText("Alpha Game") && !ui.HasText("Beta Game"), "Search should filter by project name without changing saved projects.");
                Check(f.Saved().Projects.Count == 2, "Search must not remove hidden projects.");
                await ui.ReplaceText("alpha", "no matching game");
                await ui.Wait(() => ui.HasText("No matching projects"), "Unmatched search should explain the empty result.");
                break;
            case "FavoriteProject":
                await ui.Click(ui.Text("Beta Game"), PaperMouseBtn.Right);
                await ui.ClickText("Favourite");
                await ui.Wait(() => f.Saved().Projects.Single(p => p.Name == "Beta Game").Favorite, "Favourite must persist.");
                Check(ui.Text("Beta Game").Data.Y < ui.Text("Alpha Game").Data.Y, "Favourites should appear above ordinary projects.");
                break;
            case "ChooseProjectEditor":
                await ui.ClickText("v1.0.0");
                await ui.ClickText("v1.1.0");
                await ui.Wait(() => f.Saved().Projects.Single().EditorKey == _secondEditorKey, "Choosing an editor should persist the project pin.");
                break;
            case "MissingProject":
                Check(ui.HasText("Folder unavailable"), "Missing projects should be clearly marked.");
                await ui.ClickText("Open");
                await ui.Wait(() => ui.HasText("Choose an installed editor"), "A missing pin should show an actionable launch error.");
                Check(!File.Exists(f.LaunchRecord), "Unavailable projects must not start an editor.");
                break;
            case "CancelFolderPicker":
                await ui.ClickText("Add project");
                await ui.Wait(() => Modal.IsOpen, "Add project should open the folder picker.");
                await ui.ClickText("Cancel");
                await ui.Wait(() => !Modal.IsOpen, "Cancel should close the picker.");
                Check(f.Saved().Projects.Count == 0, "Cancelling the picker must not add a project.");
                break;
            case "AddExistingProject":
            case "DuplicateProjectImport":
                for (int i = 0; i < (scenario == "DuplicateProjectImport" ? 2 : 1); i++)
                {
                    await ui.ClickText("Add project");
                    await ui.Wait(() => ui.HasText("Folder:"), "The folder picker should open.");
                    await ui.FillBesideLabel("Folder:", _project!.Path);
                    await ui.ClickText("Select Folder");
                    await ui.Wait(() => !Modal.IsOpen && ui.HasText("Imported Game"), "Choosing a folder should add its project card.");
                }
                Check(f.Saved().Projects.Single().Path == _project!.Path, "Importing the same folder twice must retain one project record.");
                Check(File.ReadAllText(Path.Combine(_project.Path, "Assets", "keep.txt")) == "User project data", "Import must preserve existing files.");
                break;
            case "InstallEditor":
                await ui.Navigate("Versions");
                // Refresh uses the deterministic HTTP catalog rather than the public network.
                await ui.ClickText("Refresh");
                await ui.ClickText("Install");
                await ui.Wait(() => f.Store.InstalledEditors().Count == 1, "Install should register a verified editor.");
                Check(f.Saved().DefaultEditorKey == f.Store.InstalledEditors().Single().Key, "The first installed editor should become the default.");
                Check(!Directory.EnumerateDirectories(f.Store.WorkPath).Any(), "Successful installation must clean up staging files.");
                break;
            case "RepairEditor":
            case "CorruptDownload":
                f.CorruptDownload = scenario == "CorruptDownload";
                await ui.Navigate("Versions");
                await ui.HoverText("v1.0.0");
                await ui.ClickText("Repair");
                if (scenario == "RepairEditor")
                {
                    await ui.Wait(() => ui.HasText("Editor repaired"), "Repair should report success.");
                    Check(new FileInfo(f.Store.ExecutablePath(_editor!)).Length > 100, "Repair should restore the executable.");
                }
                else
                {
                    await ui.Wait(() => ui.HasText("SHA-256 check"), "Corrupt downloads should show a verification error.");
                    Check(File.ReadAllText(f.Store.ExecutablePath(_editor!)) == "broken editor", "Failed repair must preserve the previous installation.");
                    Check(!Directory.EnumerateDirectories(f.Store.WorkPath).Any(), "Failed download should clean up staging files.");
                }
                break;
            case "CancelUninstall":
            case "ConfirmUninstall":
                await ui.Navigate("Versions");
                await ui.HoverText("v1.0.0");
                await ui.ClickText("Uninstall");
                await ui.Wait(() => Modal.IsOpen, "Uninstall should require confirmation.");
                await ui.ClickText(scenario == "CancelUninstall" ? "Cancel" : "Uninstall");
                if (scenario == "CancelUninstall")
                    Check(File.Exists(f.Store.ExecutablePath(_editor!)), "Cancel must keep the editor installed.");
                else
                {
                    await ui.Wait(() => f.Store.InstalledEditors().Count == 0 && f.Saved().DefaultEditorKey == null,
                        "Confirmed uninstall should remove the editor and save its default selection.");
                    Check(f.Saved().DefaultEditorKey == null, "Uninstalling the default editor must clear the default.");
                    Check(f.Saved().Projects.Single().EditorKey == _editor!.Key, "Uninstall must retain project pins for reinstalling later.");
                    Check(File.Exists(Path.Combine(_project!.Path, "Assets", "keep.txt")), "Uninstall must preserve projects.");
                }
                break;
            case "CancelDownload":
            case "CloseDuringDownload":
                await ui.Navigate("Versions");
                await ui.ClickText("Refresh");
                f.SlowDownload = true;
                await ui.ClickText("Install");
                await ui.Wait(() => f.Downloads > 0 && ui.HasText("Cancel"), "Download should expose a cancel action.");
                if (scenario == "CloseDuringDownload")
                {
                    ui.ExpectClose();
                    await ui.ClickAt(ui.Paper.Width - 21, 17);
                    await ui.Wait(() => false, "Closing should finish download cancellation and close the window.", 15);
                    return;
                }
                await ui.ClickText("Cancel");
                await ui.Wait(() => ui.HasText("Existing installations are unchanged."), "Cancellation should report a safe outcome.");
                Check(f.Store.InstalledEditors().Count == 0, "Cancelled download must not register an editor.");
                Check(!Directory.EnumerateDirectories(f.Store.WorkPath).Any(), "Cancelled download must leave no partial installation.");
                break;
            case "SettingsPersist":
                await ui.Navigate("Settings");
                await ui.Scroll();
                await ui.ClickText("Show FPS");
                await ui.ClickText("Close on launch");
                await ui.Wait(() => f.Saved().ShowFps && f.Saved().CloseOnEditorLaunch, "Preference toggles must persist to disk.");
                break;
            case "SettingsAfterRestart":
                Check(f.Store.Settings.ShowFps && f.Store.Settings.CloseOnEditorLaunch, "Restart must restore saved settings.");
                await ui.Navigate("Settings");
                await ui.Scroll();
                await ui.ClickText("Show FPS");
                await ui.ClickText("Close on launch");
                await ui.Wait(() => !f.Saved().ShowFps && !f.Saved().CloseOnEditorLaunch, "Restored switches must start checked and toggle off.");
                break;
            case "ManualUpdateChangelog":
            case "ManualReoffersDismissedUpdate":
                await OpenManualUpdate(ui);
                Check(ui.HasText("Fixture release notes") && ui.HasText("Changelog"), "The update dialog must render its release notes.");
                await ui.ClickText("Cancel");
                await ui.Wait(() => LauncherUpdateCheckService.IsDismissed(f.Saved(), f.Store.Settings.LauncherRepository, 201), "Dismissing an update must save its repository and release ID.");
                break;
            case "EnablePrereleases":
                await ui.Navigate("Settings");
                await ui.Scroll();
                await ui.ClickText("Opt in to launcher prereleases");
                await ui.Wait(() => ui.HasText("3.0.0-preview.1") && Modal.IsOpen, "Opting into previews should immediately offer the latest preview.");
                Check(f.Saved().LauncherPrereleases && f.LauncherChecks > 0, "Opt-in and its update check must both occur.");
                await ui.ClickText("Cancel");
                await ui.Wait(() => LauncherUpdateCheckService.IsDismissed(f.Saved(), f.Store.Settings.LauncherRepository, 202), "Preview dismissal must be saved.");
                break;
            case "StartupDismissFirst":
                await ui.Wait(() => Modal.IsOpen && ui.HasText("Fixture release notes"), "Startup should check for updates and show their changelog.");
                await ui.ClickText("Cancel");
                await ui.Wait(() => LauncherUpdateCheckService.IsDismissed(f.Saved(), f.Store.Settings.LauncherRepository, 201), "Startup dismissal must persist.");
                break;
            case "StartupDismissSecond":
                await ui.Wait(() => f.LauncherChecks > 0, "Restart should still check for newer releases.");
                await ui.Frame(20);
                Check(!Modal.IsOpen, "Restart must not prompt again for the dismissed release.");
                break;
            case "AcceptHealthyUpdate":
                await OpenManualUpdate(ui);
                ui.ExpectClose();
                await ui.ClickText("Update and restart");
                await ui.Wait(() => f.Saved().LauncherExecutable != null, "The healthy update should become the startup target.");
                await ui.Wait(() => false, "A healthy update should close the original window.", 15);
                break;
            case "FailedUpdateStartup":
                await OpenManualUpdate(ui);
                await ui.ClickText("Update and restart");
                await ui.Wait(() => ui.HasText("Couldn't complete"), "An early update crash must be reported without closing the launcher.");
                Check(f.Saved().LauncherExecutable == null, "Failed startup must not activate the update.");
                Check(f.Saved().Locale == "en", "Failed startup must preserve preferences.");
                break;
            default: throw new ArgumentException("Unknown E2E scenario: " + scenario);
        }
    }

    private static async Task OpenManualUpdate(UiDriver ui)
    {
        await ui.Navigate("Settings");
        await ui.Scroll();
        await ui.ClickText("Check for updates");
        await ui.Wait(() => Modal.IsOpen && ui.HasText("Fixture release notes"), "Manual check should show the update dialog and changelog.");
    }

    private static void AssertLaunch(LauncherFixture fixture, string project)
    {
        using JsonDocument record = JsonDocument.Parse(File.ReadAllText(fixture.LaunchRecord));
        Check(record.RootElement.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["--project", project]),
            "Editor arguments must preserve the complete project path, including spaces.");
        Check(record.RootElement.GetProperty("WorkingDirectory").GetString()!.Contains("Probe"), "The editor must start from its installation directory.");
    }

    internal static void AfterClose(string scenario, LauncherFixture fixture)
    {
        if (scenario == "AcceptHealthyUpdate")
            Check(fixture.Saved().LauncherExecutable is { } path && File.Exists(path), "Only a healthy update may be activated before closing.");
        if (scenario == "CloseDuringDownload")
            Check(fixture.Store.InstalledEditors().Count == 0 && !Directory.EnumerateDirectories(fixture.Store.WorkPath).Any(),
                "Closing during download must wait for cancellation and cleanup.");
        if (scenario == "CloseOnEditorLaunch")
            Check(fixture.Saved().Projects.Single().LastOpened != default, "Close-on-launch must still persist project history.");
    }
}
