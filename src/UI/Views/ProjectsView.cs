using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>The project list with its toolbar, or the new-project form while that is open.</summary>
sealed class ProjectsView(
    LauncherStore store,
    Controls ui,
    Dialogs dialogs,
    EditorCoordinator editors,
    NotificationCenter notifications,
    LogsDialog logs,
    NewProjectView newProject
)
{
    private string _search = "";

    public bool ShowingNewProject => newProject.IsOpen;

    public void Draw(Paper p)
    {
        if (newProject.IsOpen)
        {
            newProject.Draw(p);
            return;
        }

        ui.Label(p, "projects-title", "launcher.projects.title", 18, ui.Ink, 30, true);
        Project[] projects = store.Settings.Projects.Where(project => (project.Name + project.Path).Contains(_search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(project => project.Favorite)
            .ThenByDescending(project => project.LastOpened)
            .ToArray();
        if (projects.Length == 0)
        {
            using (ui.Card(p, "empty"))
            {
                ui.Label(
                    p,
                    "empty-title",
                    _search.Length > 0 ? "launcher.projects.no_matches" : "launcher.projects.empty_title",
                    23,
                    ui.Ink,
                    42,
                    true
                );
                ui.Label(
                    p,
                    "empty-help",
                    "launcher.projects.empty_description",
                    14,
                    ui.Muted,
                    30
                );
            }
        }

        foreach (Project project in projects)
        {
            string id = "project-" + project.Path;
            bool folderExists = Directory.Exists(project.Path);
            bool wide = Controls.IsWideLayout(p);
            using (ui.VersionCard(p, id))
            {
                Origami.RightClickMenu(
                    p,
                    id + "menu",
                    menu =>
                    {
                        if (folderExists)
                        {
                            menu.Item(Loc.Get("launcher.projects.show_in_folder"), () => ProcessHelper.OpenInShell(project.Path));
                        }

                        menu.Item(Loc.Get("launcher.projects.copy_path"), () => p.SetClipboard(project.Path));
                        menu.Item(
                            Loc.Get(project.Favorite ? "launcher.projects.unfavourite" : "launcher.projects.favourite"),
                            () =>
                            {
                                project.Favorite = !project.Favorite;
                                store.Save();
                            }
                        );
                        menu.Item(
                            Loc.Get("launcher.projects.remove"),
                            () =>
                            {
                                store.Settings.Projects.Remove(project);
                                store.Save();
                            }
                        );
                    }
                );
                using ((wide ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Clip()
                        .Enter())
                    {
                        ui.Label(p, id + "name", project.Name, 19, ui.Ink, 28, true);
                        ElementBuilder path = p.Box(id + "path")
                            .Height(24)
                            .Text(project.Path, ui.Font)
                            .FontSize(16)
                            .TextColor(ui.Muted)
                            .Alignment(TextAlignment.MiddleLeft);
                        if (folderExists)
                        {
                            path.Cursor(PaperCursor.Pointer)
                                .Tooltip(Loc.Get("launcher.projects.show_in_folder"))
                                .OnClick(_ =>
                                {
                                    if (Directory.Exists(project.Path))
                                    {
                                        ProcessHelper.OpenInShell(project.Path);
                                    }
                                });
                            path.Hovered.TextColor(ui.Appearance.Accent);
                        }
                        else
                        {
                            path.IsNotInteractable();
                        }
                        if (project.LastOpened != default)
                        {
                            ui.Label(p, id + "last-opened", RelativeDate.FormatElapsed(project.LastOpened), 12, ui.Muted, 22);
                        }

                        if (!folderExists)
                        {
                            ui.Label(p, id + "missing", "launcher.projects.folder_unavailable", 12, ui.Appearance.Theme.Red.C500, 24);
                        }
                    }

                    using (p.Column(id + "actions")
                        .Width(wide ? UnitValue.Pixels(248) : UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .Gap(8)
                        .Enter())
                    {
                        using (p.Row(id + "buttons")
                            .Height(40)
                            .Gap(8)
                            .JustifyContent(LayoutJustification.End)
                            .Enter())
                        {
                            ui.Button(p, id + "remove", "launcher.projects.remove", token => RemoveProjectAsync(project, token), width: 90);
                            ui.Button(p, id + "open", "launcher.projects.open", token => OpenProjectAsync(project, token), true, 90);
                        }

                        string[] keys = editors.Installed.Select(e => e.Key).ToArray();
                        Origami.Dropdown(
                                p,
                                id + "editor",
                                project.EditorKey ?? "",
                                value =>
                                {
                                    project.EditorKey = value;
                                    store.Save();
                                },
                                keys
                            )
                            .Display(key => editors.Installed.FirstOrDefault(e => e.Key == key)?.ToString() ?? Loc.Get("launcher.projects.choose_editor"))
                            .Width(UnitValue.Stretch())
                            .IsItemEnabled(_ => !ui.Busy)
                            .Show();
                    }
                }
            }
        }
    }

    public void DrawToolbar(Paper p)
    {
        using (p.Row("toolbar").Height(44).Gap(10).Enter())
        {
            ui.Button(p, "add", "launcher.projects.add", AddProjectAsync, true, 125);
            ui.Button(p, "new", "launcher.projects.new", newProject.ToggleAsync, width: 138);
            p.Box("toolbar-spacer");

            bool hasLogs = editors.Sessions.Any(session => session.Lines.Count > 0);
            if (hasLogs)
            {
                Origami.Button(p, "project-logs", Loc.Get("launcher.console.logs"))
                    .Width(76).Height(40).Variant(OrigamiVariant.Subtle).OnClick(logs.Show).Show();
            }
            float searchWidth = Math.Clamp(Controls.ContentWidth(p) - 380 - (hasLogs ? 86 : 0), 140, 240);
            Origami.TextField(p, "search", _search, value => _search = value)
                .Width(searchWidth)
                .Placeholder(Loc.Get("launcher.projects.search"))
                .Show();
        }
    }

    private async Task AddProjectAsync(CancellationToken token)
    {
        if (await dialogs.PickFolderAsync(EditorSettings.ProjectsDirectory(), token) is {} path)
        {
            notifications.Notify("launcher.projects.added", store.AddProject(path).Name);
        }
    }

    private Task RemoveProjectAsync(Project project, CancellationToken token)
    {
        store.Settings.Projects.Remove(project);
        store.Save();
        notifications.Notify("launcher.projects.removed", "launcher.projects.files_unchanged", ToastType.Info);
        return Task.CompletedTask;
    }

    private Task OpenProjectAsync(Project project, CancellationToken token)
    {
        InstalledEditor editor = editors.Installed.FirstOrDefault(editor => editor.Key == project.EditorKey)
                                 ?? throw new InvalidOperationException(Loc.Get("launcher.errors.choose_installed_editor"));
        return editors.LaunchAsync(editor, project, token);
    }
}
