using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using TextWrapMode=Prowl.Scribe.TextWrapMode;

namespace Prowl.Launcher;

/// <summary>Installed editors, the source build and the releases available to install.</summary>
sealed class VersionsView(LauncherStore store, Controls ui, Dialogs dialogs, EditorCoordinator editors)
{
    public void Draw(Paper p)
    {
        using (p.Row("installed-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            ui.Label(p, "installed-title", "launcher.versions.installed_title", 18, ui.Ink, 30, true);
            ui.Button(p, "folder", "launcher.versions.install_folder", editors.OpenInstallFolderAsync, width: 130);
        }

        if (editors.Installed.Count == 0)
        {
            ui.Label(p, "none", "launcher.versions.none_installed", 14, ui.Muted, 30);
        }

        foreach (InstalledEditor editor in editors.Installed)
        {
            string id = "installed-" + editor.Key;
            bool wide = Controls.IsWideLayout(p);
            using (ui.VersionCard(p, id))
            {
                bool showMaintenance = p.IsParentHovered;
                using ((wide ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Enter())
                    {
                        using (p.Row(id + "title").Height(32).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                        {
                            p.Box(id + "name")
                                .Width(UnitValue.Auto)
                                .Height(32)
                                .IsNotInteractable()
                                .Text(editor.Tag, ui.Bold)
                                .FontSize(19.8f)
                                .TextColor(ui.Ink)
                                .Alignment(TextAlignment.MiddleLeft);

                            bool isDefault = editor.Key == store.Settings.DefaultEditorKey;
                            string hint = isDefault
                                ? "launcher.versions.default_hint"
                                : "launcher.versions.set_default_hint";

                            Origami.Button(p, id + "default", "")
                                .LeadingIcon(isDefault
                                    ? LauncherIcons.FilledStar.Tinted(Color.Gold)
                                    : OrigamiIconSet.Star.Tinted(ui.Muted))
                                .Width(28)
                                .Height(28)
                                .Subtle()
                                .Tooltip(Loc.Get(hint))
                                .Disabled(ui.Busy)
                                .OnClick(() => editors.SetDefault(editor, !isDefault))
                                .Show();

                            if (editor.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase)
                                && editors.Releases.FirstOrDefault(release => release.Id == editor.ReleaseId) is {} installedRelease)
                            {
                                dialogs.ChangelogButton(p, id + "notes", installedRelease, true);
                            }

                        }
                        ui.Label(
                            p,
                            id + "meta",
                            Loc.Get("launcher.versions.installed_metadata", new
                            {
                                platform = editor.Platform, date = editor.InstalledAt.LocalDateTime.ToString("d")
                            }),
                            12,
                            ui.Muted,
                            22
                        );
                    }

                    using (p.Row(id + "actions")
                        .Width(wide ? UnitValue.Auto : UnitValue.Stretch())
                        .MaxWidth(wide ? UnitValue.Pixels(Controls.ContentWidth(p) - 250) : UnitValue.Percentage(100))
                        .Height(UnitValue.Auto)
                        .WrapContent()
                        .Gap(8)
                        .LineGap(8)
                        .JustifyContent(LayoutJustification.End)
                        .Enter())
                    {
                        if (showMaintenance)
                        {
                            ui.Button(p, id + "uninstall", "launcher.versions.uninstall", token => editors.UninstallAsync(editor, token), width: 110);
                            if (!editor.IsMain)
                            {
                                ui.Button(p, id + "repair", "launcher.versions.repair",
                                    token => editors.RepairAsync(editor, token), width: 90);
                            }
                        }
                        ui.Button(p, id + "launch", "launcher.versions.launch", token => editors.LaunchAsync(editor, null, token), true, 90);
                    }
                }

            }
        }

        using (p.Row("available-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            ui.Label(p, "available-title", "launcher.versions.available_title", 18, ui.Ink, 35, true);

            using (p.Row("include-editor-prereleases-control").Width(UnitValue.Auto).Height(40)
                .Gap(8).AlignItems(LayoutAlignment.Center).Enter())
            {
                p.Box("include-editor-prereleases-label").Width(UnitValue.Auto).Height(24)
                    .Text(Loc.Get("launcher.versions.include_prereleases"), ui.Font)
                    .FontSize(ui.Appearance.Theme.Metrics.FontSize).TextColor(ui.Ink)
                    .Alignment(TextAlignment.MiddleLeft)
                    .Cursor(ui.Busy ? PaperCursor.Default : PaperCursor.Pointer)
                    .OnClick(click =>
                    {
                        if (!ui.Busy)
                        {
                            _ = editors.SetIncludePrereleasesAsync(!editors.IncludePrereleases);
                        }
                    });
                Origami.Toggle(p, "include-editor-prereleases", editors.IncludePrereleases, value => _ = editors.SetIncludePrereleasesAsync(value))
                    .NoLabel()
                    .Disabled(ui.Busy).Show();
            }
            ui.Button(p, "refresh", "launcher.versions.refresh", token => editors.RefreshAsync(token), width: 96);
        }

        if (editors.IncludePrereleases)
        {
            using (ui.VersionCard(p, "main"))
            {
                bool wide = Controls.IsWideLayout(p);
                using ((wide ? p.Row("main-row") : p.Column("main-row"))
                    .Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column("main-info").Height(UnitValue.Auto).Gap(2).Enter())
                    {
                        ui.Label(p, "main-title", "launcher.versions.main_title", 18, ui.Ink, 32, true);
                        p.Box("main-description")
                            .Height(UnitValue.Auto)
                            .IsNotInteractable()
                            .Text(Loc.Get("launcher.versions.main_description"), ui.Font)
                            .FontSize(16)
                            .TextColor(ui.Muted)
                            .Wrap(TextWrapMode.Wrap);
                    }
                    using (p.Row("main-actions")
                        .Width(wide ? UnitValue.Auto : UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .JustifyContent(LayoutJustification.End)
                        .Enter())
                    {
                        DrawSourceBuildButton(p, "main-build", primary: true);
                    }
                }
            }
        }

        HashSet<long> installedReleaseIds = editors.Installed
            .Where(e => e.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ReleaseId).ToHashSet();
        EditorRelease[] available = editors.Releases.Where(r => !installedReleaseIds.Contains(r.Id) && r.AssetFor(Platform.Identifier) != null && (editors.IncludePrereleases || !r.Preview))
            .ToArray();
        if (available.Length == 0)
        {
            ui.Label(p, "no-releases", "launcher.versions.none_available", 14, ui.Muted, 35);
        }

        foreach (EditorRelease release in available)
        {
            string id = "release-" + release.Id;
            using (ui.VersionCard(p, id))
            {
                using ((Controls.IsWideLayout(p) ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
                    .AlignItems(LayoutAlignment.Center)
                    .Gap(12)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Enter())
                    {
                        using (p.Row(id + "title").Height(28).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                        {
                            p.Box(id + "name")
                                .Width(UnitValue.Auto).Height(28)
                                .IsNotInteractable()
                                .Text(release.Tag, ui.Bold).FontSize(19.8f)
                                .TextColor(ui.Ink).Alignment(TextAlignment.MiddleLeft);
                            Color tagColor = release.Preview ? ui.Appearance.Theme.Amber.C500 : ui.Muted;
                            p.Box(id + "channel")
                                .Width(UnitValue.Auto).Height(24).Padding(8, 0)
                                .Rounded(ui.Appearance.Theme.Metrics.Rounding)
                                .BackgroundColor(OrigamiTheme.WithAlpha(tagColor, 24))
                                .IsNotInteractable()
                                .Text(Loc.Get(release.Preview ? "launcher.versions.preview" : "launcher.versions.stable"), ui.Font)
                                .FontSize(14).TextColor(tagColor)
                                .Alignment(TextAlignment.MiddleCenter);
                        }
                        ui.Label(
                            p,
                            id + "meta",
                            $"{release.Published.LocalDateTime:d} · {release.AssetFor(Platform.Identifier)!.Size / 1048576d:F0} MB",
                            12,
                            ui.Muted,
                            22
                        );
                    }

                    dialogs.ChangelogButton(p, id + "notes", release);
                    ui.Button(p, id + "install", "launcher.versions.install", token => editors.InstallAsync(release, token), true, 90);
                }
            }
        }

    }

    private void DrawSourceBuildButton(Paper p, string id, string? repository = null, bool primary = false)
    {
        const string label = "launcher.versions.main_build";
        string text = Loc.Get(label);
        float width = Math.Max(154, text.Length * ui.Appearance.Theme.Metrics.FontSize * 0.7f + 48);
        Origami.Button(p, id, text)
            .Width(width)
            .Height(40)
            .Rounding(ui.Appearance.Theme.Metrics.Rounding)
            .LeadingIcon(LauncherIcons.ForAction(label))
            .Variant(primary ? OrigamiVariant.Primary : OrigamiVariant.Default)
            .Disabled(ui.Busy)
            .OnClick(() => ui.Operations.Start(token => editors.BuildMainAsync(token, repository), label))
            .Show();
    }
}
