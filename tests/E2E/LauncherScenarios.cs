using System.Text.Json;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using static Prowl.Launcher.Test.E2E.UiDriver;

namespace Prowl.Launcher.Test.E2E;

static class LauncherScenarios
{
    private static InstalledEditor? _editor;
    private static Project? _project;
    private static string? _secondEditorKey;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendWindowMessage(nint window, uint message, nint wParam, nint lParam);

    private static nint WindowIconHandle(Launcher launcher)
    {
        object frame = GetField<object>(launcher, "_windowFrame");
        return SendWindowMessage(GetField<nint>(frame, "_handle"), 0x007F, 1, 0); // WM_GETICON, ICON_BIG
    }

    internal static async Task Prepare(string scenario, LauncherFixture fixture)
    {
        if (scenario == "ReadLocalNews") fixture.AddProject("News placement project");
        if (scenario == "EditorUpdateIndicator")
        {
            _editor = await fixture.InstallEditor();
            fixture.EditorReleases.Clear();
            fixture.EditorReleases.Add(fixture.EditorRelease with
            {
                Id = 12,
                Tag = "v1.1.0-preview.1",
                Preview = true,
                Assets =
                [
                    fixture.EditorRelease.Assets[0] with
                    {
                        Name = $"Prowl-v1.1.0-preview.1-{Platform.Identifier}.zip"
                    }
                ]
            });
        }
        if (scenario.StartsWith("Sample", StringComparison.Ordinal))
        {
            fixture.PublishSamples();
            LauncherUpdateCheckService.Dismiss(fixture.Store, fixture.Store.Settings.LauncherRepository, 201);
            if (scenario is "SampleUpdateAvailable" or "SampleAlreadyLatest")
            {
                await new SampleService(fixture.Http, fixture.Store).EnsureDownloadedAsync();
            }
            if (scenario == "SampleUpdateAvailable")
            {
                fixture.PublishSamples(newer: true);
            }
        }
        string[] installedScenarios =
        [
            "CreateProjectAndOpenIt", "RejectInvalidProjectName", "RejectExistingProjectFolder",
            "OpenPinnedProject", "ChooseProjectEditor", "RepairEditor", "CancelUninstall", "ConfirmUninstall",
            "CorruptDownload", "CloseOnEditorLaunch"
        ];
        if (installedScenarios.Contains(scenario))
        {
            _editor = await fixture.InstallEditor();
        }
        string[] projectScenarios =
        [
            "OpenPinnedProject", "RemoveProject", "FavoriteProject", "ChooseProjectEditor",
            "MissingProject", "ConfirmUninstall", "CloseOnEditorLaunch"
        ];
        if (projectScenarios.Contains(scenario))
        {
            _project = fixture.AddProject("Alpha Game", _editor);
        }
        if (scenario == "SearchProjects")
        {
            fixture.AddProject("Alpha Game");
            fixture.AddProject("Beta Game");
        }
        if (scenario == "FavoriteProject")
        {
            fixture.AddProject("Beta Game");
        }
        if (scenario is "AddExistingProject" or "DuplicateProjectImport")
        {
            _project = fixture.AddProject("Imported Game");
            fixture.Store.Settings.Projects.Clear();
            fixture.Store.Save();
        }
        if (scenario == "MissingProject")
        {
            Directory.Delete(_project!.Path, true);
        }
        if (scenario == "RepairEditor" || scenario == "CorruptDownload")
        {
            File.WriteAllText(fixture.Store.ExecutablePath(_editor!), "broken editor");
        }
        if (scenario == "RejectExistingProjectFolder")
        {
            Directory.CreateDirectory(Path.Combine(fixture.Projects, "My Game"));
            File.WriteAllText(Path.Combine(fixture.Projects, "My Game", "keep.txt"), "Existing data");
        }
        if (scenario == "ChooseProjectEditor")
        {
            EditorRelease release = fixture.EditorRelease with
            {
                Id = 12,
                Tag = "v1.1.0",
                Assets =
                [
                    fixture.EditorRelease.Assets[0] with
                    {
                        Name = $"Prowl-v1.1.0-{Platform.Identifier}.zip"
                    }
                ]
            };
            _secondEditorKey = (await new EditorInstallerService(fixture.Http, fixture.Store).InstallAsync(release, Platform.Identifier)).Key;
        }
        if (scenario == "ManualReoffersDismissedUpdate")
        {
            LauncherUpdateCheckService.Dismiss(fixture.Store, fixture.Store.Settings.LauncherRepository, 201);
        }
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
            case "WindowIcon":
                if (OperatingSystem.IsWindows())
                {
                    Check(WindowIconHandle(ui.Launcher) != 0, "The native launcher window must have an icon, including when launched through dotnet.");
                }
                break;
            case "ToastsAboveProgress":
                using (CancellationTokenSource operation = new())
                {
                    SetField(ui.Launcher, "_operation", operation);
                    SetField(ui.Launcher, "_immediateProgress", true);
                    var progressNotifications = GetField<System.Collections.Concurrent.ConcurrentQueue<(string Title, string Message, ToastType Type)>>(ui.Launcher, "_notifications");
                    progressNotifications.Enqueue(("Toast above progress", "Cancel remains accessible", ToastType.Error));
                    await ui.Frame(2);
                    ElementHandle progressToast = ui.Text("Toast above progress").GetParentHandle().GetParentHandle().GetParentHandle();
                    float toastTop = progressToast.Data.Y;
                    Check(toastTop + progressToast.Data.LayoutHeight < ui.Paper.Height - Constants.Layout.OperationHeight,
                        "Toasts must stay above the entire progress panel.");
                    await ui.ClickText("Cancel");
                    Check(operation.IsCancellationRequested, "Cancel must remain clickable while a toast is visible.");
                    SetField(ui.Launcher, "_operation", null!);
                    await ui.Frame(2);
                    progressToast = ui.Text("Toast above progress").GetParentHandle().GetParentHandle().GetParentHandle();
                    Check(Math.Abs(progressToast.Data.Y - toastTop - Constants.Layout.OperationHeight) < 1,
                        "Toasts must return to the bottom when the progress panel closes.");
                }
                break;
            case "DismissToasts":
                var notifications = GetField<System.Collections.Concurrent.ConcurrentQueue<(string Title, string Message, ToastType Type)>>(ui.Launcher, "_notifications");
                notifications.Enqueue(("Dismiss this toast", "First notification", ToastType.Success));
                notifications.Enqueue(("Keep this toast", "Second notification", ToastType.Error));
                await ui.Frame(2);
                ElementHandle toastCard = ui.Text("Dismiss this toast").GetParentHandle().GetParentHandle().GetParentHandle();
                ElementHandle closeToast = ui.Nodes().Single(n => n.GetParentHandle() == toastCard && n.Data.OnClick != null);
                await ui.Click(closeToast);
                await ui.Frame(2);
                Check(!ui.HasText("Dismiss this toast"), "Clicking the close target must dismiss its toast immediately.");
                Check(ui.HasText("Keep this toast"), "Dismissing one toast must preserve the other toast.");
                await ui.Wait(() => !ui.HasText("Keep this toast"), "Undismissed toasts must still expire automatically.");
                break;
            case "ReadLocalNews":
                GetField<LauncherAppearance>(ui.Launcher, "_appearance").Theme.Metrics.ContainerRounding = 12;
                string newsFixture = Path.Combine(AppContext.BaseDirectory, "NewsFixture");
                string localNewsRoot = Path.Combine(f.Root, "news");
                foreach (string file in Directory.EnumerateFiles(newsFixture, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(localNewsRoot, Path.GetRelativePath(newsFixture, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
                NewsService localNews = new(f.Http, f.Store, f.Store.Settings.LauncherRepository,
                    localNewsRoot);
                SetField(ui.Launcher, "_newsRepository", f.Store.Settings.LauncherRepository);
                SetField(ui.Launcher, "_news", localNews);
                SetField(ui.Launcher, "_newsPosts", localNews.ReadCache());
                await ui.Navigate("Projects");
                await ui.Wait(() => ui.HasText("Markdown showcase (test post)"), "The local index should be readable while offline.");
                Check(ui.Text("Markdown showcase (test post)").Data.Y > ui.Text("News placement project").Data.Y,
                    "News must appear below the project list.");
                NewsImages thumbnails = GetField<NewsImages>(ui.Launcher, "_newsThumbnails");
                await ui.Wait(() => GetField<Dictionary<string, TextureTK?>>(thumbnails, "_textures").Values.Any(texture => texture != null),
                    "The optional thumbnail should load from the local news directory.");
                await ui.Frame(3);
                Check(ui.Text("Rendering notes (test post)").Data.Y > ui.Paper.Height - 220,
                    "The latest updates must remain at the bottom of the viewport.");
                ElementHandle previewThumbnail = ui.Nodes().First(node => node.Data.LayoutWidth == 96 && node.Data.LayoutHeight == 88);
                ElementHandle previewCard = previewThumbnail.GetParentHandle();
                Check(Math.Abs(previewThumbnail.Data.X - previewCard.Data.X) <= 1
                    && Math.Abs(previewThumbnail.Data.Y - previewCard.Data.Y) <= 1,
                    "Thumbnails should be flush with the preview card's left and top edges.");
                Check(ui.Text("Rendering notes (test post)").Data.X < ui.Text("Physics playground (test post)").Data.X
                    + previewCard.Data.LayoutWidth + 40,
                    "Text-only previews should use the space otherwise occupied by a thumbnail.");
                Check(Math.Abs(ui.Text("Markdown showcase (test post)").Data.Y - ui.Text("Physics playground (test post)").Data.Y) < 10,
                    "Latest updates should share a horizontal row.");
                Check(GetField<float>(ui.Launcher, "_newsFooterHeight") <= 112,
                    "The footer must leave most of the page available for projects.");
                ElementHandle firstPreview = ui.Text("Markdown showcase (test post)").GetParentHandle().GetParentHandle();
                ElementHandle lastPreview = ui.Text("Rendering notes (test post)").GetParentHandle().GetParentHandle();
                ElementHandle previewRow = firstPreview.GetParentHandle();
                float groupCenter = (firstPreview.Data.X + lastPreview.Data.X + lastPreview.Data.LayoutWidth) / 2;
                Check(Math.Abs(groupCenter - (previewRow.Data.X + previewRow.Data.LayoutWidth / 2)) <= 1,
                    "The paging arrows must not shift the cards away from the row's center.");
                ElementHandle moreButton = ui.Text("Older posts");
                Check(!ui.Text("Newer posts").IsValid, "The first page must hide the newer arrow.");
                Check(moreButton.Data.X - (lastPreview.Data.X + lastPreview.Data.LayoutWidth) >= 7,
                    "The paging arrows must have a visible gap from the rightmost news card.");
                Check(ui.HasText(RelativeDate.Format(localNews.ReadCache()[0].Date)), "The preview must show a relative date.");
                Check(ui.Text("Prowl team").Data.Y > ui.Text("Rendering notes (test post)").Data.Y,
                    "The author must appear below the title.");
                ElementHandle bylineAuthor = ui.Text("Prowl team");
                Check(Math.Abs(bylineAuthor.Data.Y + bylineAuthor.Data.LayoutHeight
                    - (lastPreview.Data.Y + lastPreview.Data.LayoutHeight - 12)) <= 1,
                    "The byline should use the bottom of the card's text area.");
                ElementHandle bylineDate = ui.Nodes().First(node => node.Data.Paragraph == RelativeDate.Format(localNews.ReadCache()[2].Date));
                Check(bylineDate.Data.X > bylineAuthor.Data.X
                    && Math.Abs(bylineDate.Data.Y - bylineAuthor.Data.Y) <= 1,
                    "Author and date must share a line with the date on the right.");
                ui.SaveScreenshot(Path.Combine(f.Root, "projects-news.png"));
                Check(!ui.Text("Refresh").IsValid && !ui.Text("News").IsValid, "The footer must not need a heading or refresh control.");
                await ui.Wait(() => !GetField<bool>(ui.Launcher, "_newsLoading"), "Local refresh should finish without a network request.");
                await ui.ClickText("Markdown showcase (test post)");
                await ui.Wait(() => ui.HasText("This is a test post"), "The local Markdown article should render.");
                NewsImages images = GetField<NewsImages>(ui.Launcher, "_newsImages");
                await ui.Wait(() => GetField<Dictionary<string, TextureTK?>>(images, "_textures").Values.Count(texture => texture != null) == 2,
                    "Both relative local screenshots should become real textures.");
                Check(GetField<Dictionary<string, TextureTK?>>(images, "_textures").Values.Where(texture => texture != null)
                    .All(texture => texture!.Width > 0 && texture.Height > 0), "Decoded screenshots must have valid dimensions.");
                await ui.Frame(3);
                AssertNewsDialogCentered(ui);
                Check(!ui.HasText("A little README fanciness"), "The article must omit its leading title.");
                Check(!ui.Nodes().Any(node => node.Data.Layer > Layer.Overlay
                    && node.Data.Paragraph == "Prowl team"),
                    "The dialog body must not repeat the author.");
                Check(ui.Nodes().Count(node => node.Data.Paragraph == "Markdown showcase (test post)"
                    && node.GetParentHandle().GetParentHandle().Data.Layer > Layer.Overlay) == 1,
                    "The title must appear once in the dialog header.");
                await ui.Resize(1000, 850);
                AssertNewsDialogCentered(ui);
                await ui.Resize(1200, 1100);
                ui.SaveScreenshot(Path.Combine(f.Root, "news-article.png"));
                await ui.ClickText("Close");
                Check(!Modal.IsOpen && ui.HasText("News placement project"), "Closing an article must leave the project list available.");
                await ui.Wait(() => ui.HasText("Markdown showcase (test post)"), "Back should return to the news cards.");
                NewsPost latest = localNews.ReadCache()[0];
                List<NewsPost> archivePosts = [latest];
                for (int i = 1; i <= 4; i++)
                {
                    NewsPost archived = new($"archive-{i}.md", $"Archived update {i}", latest.Date.AddDays(-i));
                    archivePosts.Add(archived);
                    await File.WriteAllTextAsync(Path.Combine(localNewsRoot, archived.File), "# Archived showcase " + i + "\n\nArchived content " + i);
                }
                LauncherStore.WriteJson(Path.Combine(localNewsRoot, "index.json"), archivePosts);
                await ui.Wait(() => ui.HasText("Archived update 2"), "Local index edits should appear automatically.");
                await ui.Frame(3);
                Check(!ui.HasText("Archived update 3") && !ui.HasText("Archived update 4"),
                    "The footer should show only the three latest posts.");
                await ui.Wait(() => !ui.Text("Offline").IsValid, "The startup toast must clear the archive link before clicking.");
                await ui.ClickText("Older posts");
                Check(GetField<long>(ui.Launcher, "_newsSlideStarted") != 0,
                    "Paging must start a slide transition.");
                await ui.Wait(() => ui.HasText("Archived update 4"), "The next page must show older posts.");
                await ui.Frame();
                ui.SaveScreenshot(Path.Combine(f.Root, "news-sliding.png"));
                await ui.Wait(() => !ui.HasText("Markdown showcase (test post)"), "The outgoing page must leave after the slide.");
                Check(!ui.Text("Older posts").IsValid && ui.Text("Newer posts").IsValid,
                    "The last page must show only the newer arrow.");
                ElementHandle olderPreview = ui.Text("Archived update 4").GetParentHandle().GetParentHandle();
                ElementHandle olderFirstPreview = ui.Text("Archived update 3").GetParentHandle().GetParentHandle();
                Check(Math.Abs(olderFirstPreview.Data.X - olderFirstPreview.GetParentHandle().Data.X) <= 1,
                    "An incomplete page must align its cards to the left.");
                Check(Math.Abs(olderPreview.Data.LayoutWidth - firstPreview.Data.LayoutWidth) <= 1,
                    "An incomplete page must keep the same card width.");
                Check(!Modal.IsOpen && !ui.HasText("Markdown showcase (test post)") && !ui.HasText("Archived update 2"),
                    "Paging must replace the three previews without opening a modal.");
                await ui.ClickText("Archived update 4");
                await ui.Wait(() => ui.HasText("Archived content 4"), "An older post should open from its preview.");
                await ui.Frame(3);
                AssertNewsDialogCentered(ui);
                await ui.ClickText("Close");
                Check(!Modal.IsOpen && ui.HasText("Archived update 4"), "Closing an article must preserve the current page.");
                await ui.ClickText("Newer posts");
                await ui.Wait(() => ui.HasText("Markdown showcase (test post)"), "The previous arrow must restore the latest posts.");
                await ui.Wait(() => !ui.HasText("Archived update 4"), "The reverse slide must finish on the latest page.");
                float footerY = ui.Text("Markdown showcase (test post)").Data.Y;
                for (int i = 0; i < 10; i++) f.AddProject("Overflow project " + i);
                await ui.Frame(3);
                Check(Math.Abs(ui.Text("Markdown showcase (test post)").Data.Y - footerY) < 1,
                    "Adding projects must not move the news footer.");
                float projectY = ui.Text("News placement project").Data.Y;
                await ui.Scroll();
                Check(ui.Text("News placement project").Data.Y < projectY,
                    "A long project list must scroll independently.");
                Check(Math.Abs(ui.Text("Markdown showcase (test post)").Data.Y - footerY) < 1,
                    "Scrolling projects must leave news in place.");
                ui.SaveScreenshot(Path.Combine(f.Root, "projects-scrolled-news.png"));
                LauncherStore.WriteJson(Path.Combine(localNewsRoot, "index.json"), new[] { latest });
                await ui.Wait(() => !ui.HasText("Archived update 1"), "The local index should update to a single post.");
                await ui.Frame(3);
                Check(!ui.Text("Older posts").IsValid && !ui.Text("Newer posts").IsValid,
                    "A single post must show no paging arrows.");
                ElementHandle singlePreview = ui.Text(latest.Title).GetParentHandle().GetParentHandle();
                Check(singlePreview.Data.LayoutWidth <= singlePreview.GetParentHandle().Data.LayoutWidth / 3,
                    "A single post must not stretch across the row.");
                Check(Math.Abs(singlePreview.Data.X - singlePreview.GetParentHandle().Data.X) <= 1,
                    "A single post must stay left-aligned.");
                ui.SaveScreenshot(Path.Combine(f.Root, "news-single-post.png"));
                break;
            case "EditorUpdateIndicator":
                bool HasVersionIndicator() => ui.Nodes().Any(node => node.Data.LayoutWidth == 10 && node.Data.LayoutHeight == 10
                                                                                                 && node.GetParentHandle().IsValid && node.GetParentHandle().Data.X == 8
                                                                                                 && node.GetParentHandle().Data.Y > 130 && node.GetParentHandle().Data.Y < 150);
                Check(!HasVersionIndicator(), "The indicator should stay hidden before a newer release is known.");
                await ui.Navigate("Versions");
                await ui.ClickText("Refresh");
                await ui.Wait(HasVersionIndicator, "A newer preview should show the Versions indicator when previews are included.");
                ElementHandle indicator = ui.Nodes().First(node => node.Data.LayoutWidth == 10 && node.Data.LayoutHeight == 10 && node.Data.X < 72);
                ElementHandle nav = indicator.GetParentHandle();
                Check(indicator.Data.X > nav.Data.X + nav.Data.LayoutWidth / 2 && indicator.Data.Y < nav.Data.Y + nav.Data.LayoutHeight / 2,
                    "The update dot should sit in the top-right corner of the Versions tab.");
                Check(indicator.Data.OnClick == null, "The update dot must leave navigation clicks to its parent tab.");
                await ui.ClickText("Include prereleases");
                await ui.Wait(() => !HasVersionIndicator(), "Stable-only selection must hide a preview-only update.");
                await ui.ClickText("Include prereleases");
                await ui.Wait(HasVersionIndicator, "Including previews should restore the indicator.");
                await ui.ClickText("Install");
                await ui.Wait(() => f.Store.InstalledEditors().Count == 2, "The preview should install through the normal Versions flow.");
                await ui.Wait(() => !HasVersionIndicator(), "Installing the newest eligible version should clear the indicator.");
                break;
            case "SampleDownloadOnce":
                await ui.Navigate("Samples");
                f.DelaySampleDownload = true;
                await ui.ClickText("Hello Prowl");
                await ui.Wait(() => ui.HasText("Downloading samples") && ui.HasText("Cancel"), "Sample download should show progress at the top.");
                Check(ui.Text("Downloading samples").Data.Y < ui.Text("Hello Prowl").Data.Y, "Download progress must appear above sample cards.");
                await ui.Wait(() => File.Exists(Path.Combine(f.Home, "sample-launch.log")), "The selected sample should launch after the shared download.");
                await ui.Wait(() => GetField<object?>(ui.Launcher, "_operation") == null, "Sample startup should finish.");
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
                await ui.Wait(() => !GetField<bool>(ui.Launcher, "_sampleUpdateChecking"), "The background sample check should finish.");
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
                    {
                        Check(File.ReadAllText(Path.Combine(f.Projects, "My Game", "keep.txt")) == "Existing data", "Existing project data must not be overwritten.");
                    }
                }
                break;
            case "OpenPinnedProject":
            case "CloseOnEditorLaunch":
                if (scenario == "CloseOnEditorLaunch")
                {
                    ui.ExpectClose();
                }
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
                await ui.Frame(3);
                AssertDialogCentered(ui, "Uninstall v1.0.0?");
                await ui.ClickText(scenario == "CancelUninstall" ? "Cancel" : "Uninstall");
                if (scenario == "CancelUninstall")
                {
                    Check(File.Exists(f.Store.ExecutablePath(_editor!)), "Cancel must keep the editor installed.");
                }
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

    private static void AssertNewsDialogCentered(UiDriver ui)
    {
        ElementHandle dialog = ui.Nodes().First(node => node.Data.Layer > Layer.Overlay
            && node.Data.LayoutWidth > 500 && node.Data.LayoutWidth < ui.Paper.Width
            && node.GetParentHandle().IsValid && node.GetParentHandle().Data.LayoutWidth >= ui.Paper.Width - 1);
        Check(Math.Abs(dialog.Data.Y + dialog.Data.LayoutHeight / 2 - ui.Paper.Height / 2) <= 1,
            "The article dialog must remain vertically centered.");
    }

    private static void AssertDialogCentered(UiDriver ui, string title)
    {
        ElementHandle dialog = ui.Nodes().Where(node => node.Data.Paragraph == title)
            .Select(node => node.GetParentHandle().GetParentHandle())
            .First(node => node.IsValid && node.Data.Layer > Layer.Overlay);
        Check(Math.Abs(dialog.Data.Y + dialog.Data.LayoutHeight / 2 - ui.Paper.Height / 2) <= 1,
            "The dialog must be vertically centered: " + title);
    }

    private static async Task OpenManualUpdate(UiDriver ui)
    {
        await ui.Navigate("Settings");
        await ui.Scroll();
        await ui.ClickText("Check for updates");
        await ui.Wait(() => Modal.IsOpen && ui.HasText("Fixture release notes"), "Manual check should show the update dialog and changelog.");
        await ui.Frame(3);
        AssertDialogCentered(ui, ui.Text("Switch to stable Prowl Launcher?").IsValid
            ? "Switch to stable Prowl Launcher?" : "Update Prowl Launcher?");
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
        {
            Check(fixture.Saved().LauncherExecutable is { } path && File.Exists(path), "Only a healthy update may be activated before closing.");
        }
        if (scenario == "CloseDuringDownload")
        {
            Check(fixture.Store.InstalledEditors().Count == 0 && !Directory.EnumerateDirectories(fixture.Store.WorkPath).Any(),
                "Closing during download must wait for cancellation and cleanup.");
        }
        if (scenario == "CloseOnEditorLaunch")
        {
            Check(fixture.Saved().Projects.Single().LastOpened != default, "Close-on-launch must still persist project history.");
        }
    }
}
