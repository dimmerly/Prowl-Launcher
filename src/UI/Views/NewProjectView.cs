using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

/// <summary>The new-project form: template, name, location and editor.</summary>
sealed class NewProjectView(
    LauncherStore store,
    Controls ui,
    Dialogs dialogs,
    EditorCoordinator editors,
    NotificationCenter notifications,
    bool openInitially = false
)
{
    private string? _directory;
    private string? _editorKey;
    private string _name = Loc.Get("launcher.projects.untitled");

    public bool IsOpen { get; set; } = openInitially;

    public void Draw(Paper p)
    {
        _directory ??= EditorSettings.ProjectsDirectory();
        _editorKey ??= store.Settings.DefaultEditorKey ?? editors.Installed.FirstOrDefault()?.Key;
        bool wide = Controls.IsWideLayout(p);
        using ((wide ? p.Row("new-project-form") : p.Column("new-project-form"))
            .Height(UnitValue.Auto)
            .Gap(18)
            .Enter())
        {
            using (p.Column("project-templates")
                .Height(UnitValue.Auto)
                .Gap(12)
                .Enter())
            {
                ui.Label(p, "templates-title", "launcher.projects.templates", 19, ui.Ink, 28, true);
                using (p.Column("blank-template")
                    .Height(UnitValue.Auto)
                    .Padding(20)
                    .Gap(10)
                    .BackgroundColor(ui.Appearance.Theme.Selected)
                    .BorderColor(ui.Appearance.Accent)
                    .BorderWidth(1)
                    .Rounded(ui.Appearance.Theme.Metrics.ContainerRounding)
                    .Enter())
                {
                    p.Box("blank-icon")
                        .Size(48, 48)
                        .Rounded(ui.Appearance.Theme.Metrics.ContainerRounding)
                        .BackgroundColor(ui.Appearance.Accent)
                        .IsNotInteractable()
                        .Icon(p, OrigamiIconSet.Document, Color.White, size: 26);
                    ui.Label(p, "blank-title", Loc.Get("launcher.projects.blank_name"), 18, ui.Ink, 28, true);
                    p.Box("blank-description")
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.projects.blank_description"), ui.Font)
                        .FontSize(16)
                        .TextColor(ui.Muted)
                        .Wrap(TextWrapMode.Wrap)
                        .IsNotInteractable();
                }
            }

            using (p.Column("new-project-config")
                .Width(wide ? UnitValue.Pixels(380) : UnitValue.Stretch())
                .Height(UnitValue.Auto)
                .Padding(20)
                .Gap(8)
                .BackgroundColor(ui.Appearance.Card)
                .BorderColor(ui.Appearance.Theme.BorderSoft)
                .BorderWidth(1)
                .Rounded(ui.Appearance.Theme.Metrics.ContainerRounding)
                .Enter())
            {
                ui.Label(p, "configure-title", Loc.Get("launcher.projects.configure"), 19, ui.Ink, 28, true);
                ui.Label(p, "new-name-label", Loc.Get("launcher.projects.name"), 14, ui.Muted, 24);
                Origami.TextField(p, "new-project-name", _name, value => _name = value)
                    .Height(40)
                    .Show();
                ui.Label(p, "new-directory-label", Loc.Get("launcher.projects.path"), 14, ui.Muted, 24);
                using (p.Row("new-location-row")
                    .Height(40)
                    .Gap(8)
                    .Enter())
                {
                    Origami.TextField(p, "new-project-directory", _directory, value => _directory = value)
                        .Height(40)
                        .Show();
                    Origami.Button(p, "browse-new-location", "")
                        .Width(40)
                        .Height(40)
                        .LeadingIcon(OrigamiIconSet.FolderOpen)
                        .Tooltip(Loc.Get("launcher.common.browse"))
                        .Disabled(ui.Busy)
                        .OnClick(() => ui.Operations.Start(PickNewProjectDirectoryAsync, "launcher.common.browse"))
                        .Show();
                }

                ui.Label(p, "new-editor-label", "launcher.projects.editor_version", 14, ui.Muted, 24);
                Origami.Dropdown(
                        p,
                        "new-editor",
                        _editorKey ?? "",
                        value => _editorKey = value,
                        editors.Installed.Select(e => e.Key).ToArray()
                    )
                    .Display(key => editors.Installed.FirstOrDefault(e => e.Key == key)?.ToString() ?? Loc.Get("launcher.projects.choose_editor"))
                    .Width(UnitValue.Stretch())
                    .IsItemEnabled(_ => !ui.Busy)
                    .Show();

                if (editors.Installed.Count == 0)
                {
                    p.Box("need-editor")
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.projects.install_editor_first"), ui.Font)
                        .FontSize(16)
                        .TextColor(ui.Muted)
                        .Wrap(TextWrapMode.Wrap)
                        .IsNotInteractable();
                }
            }
        }

        using (p.Row("create-project-actions")
            .Height(40)
            .Gap(8)
            .JustifyContent(LayoutJustification.End)
            .Enter())
        {
            ui.Button(
                p,
                "cancel-new-project",
                "launcher.common.cancel",
                _ =>
                {
                    IsOpen = false;
                    return Task.CompletedTask;
                },
                width: 100
            );
            ui.Button(p, "create-project", "launcher.projects.create_and_open", CreateProjectAsync, true, 155);
        }
    }

    public Task ToggleAsync(CancellationToken token)
    {
        IsOpen = !IsOpen;
        return Task.CompletedTask;
    }

    private async Task PickNewProjectDirectoryAsync(CancellationToken token)
    {
        if (await dialogs.PickFolderAsync(_directory ?? EditorSettings.ProjectsDirectory(), token) is {} selected)
        {
            _directory = selected;
        }
    }

    /// <summary>Creates a blank project for the chosen editor, adds it to the list and opens it.</summary>
    private async Task CreateProjectAsync(CancellationToken token)
    {
        InstalledEditor editor = editors.Installed.FirstOrDefault(e => e.Key == _editorKey)
                                 ?? throw new InvalidOperationException(Loc.Get("launcher.errors.choose_installed_editor"));
        // Validate the installation before creating anything on disk.
        editors.Validate(editor);
        string parent = _directory ?? EditorSettings.ProjectsDirectory();
        string name = _name;
        string path = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return ProjectTemplates.CreateBlank(parent, name, editor);
        }, token);

        Project project = store.AddProject(path);
        project.EditorKey = editor.Key;
        store.Save();
        IsOpen = false;
        notifications.Notify("launcher.projects.created", project.Name);
        await editors.LaunchAsync(editor, project, token);
    }
}
