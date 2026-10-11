using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using TextWrapMode=Prowl.Scribe.TextWrapMode;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawVersions(Paper p)
    {
        using (p.Row("installed-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            Label(p, "installed-title", "launcher.versions.installed_title", 18, Ink, 30, true);
            Button(p, "folder", "launcher.versions.install_folder", OpenInstallFolderAsync, width: 130);
        }

        if (_installed.Count == 0)
        {
            Label(p, "none", "launcher.versions.none_installed", 14, Muted, 30);
        }

        foreach (InstalledEditor editor in _installed)
        {
            string id = "installed-" + editor.Key;
            bool wide = IsWideLayout(p);
            using (VersionCard(p, id))
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
                                .Text(editor.Tag, _bold)
                                .FontSize(19.8f)
                                .TextColor(Ink)
                                .Alignment(TextAlignment.MiddleLeft);

                            bool isDefault = editor.Key == store.Settings.DefaultEditorKey;
                            string hint = isDefault
                                ? "launcher.versions.default_hint"
                                : "launcher.versions.set_default_hint";

                            Origami.Button(p, id + "default", "")
                                .LeadingIcon(isDefault
                                    ? LauncherIcons.FilledStar.Tinted(Color.Gold)
                                    : OrigamiIconSet.Star.Tinted(Muted))
                                .Width(28)
                                .Height(28)
                                .Subtle()
                                .Tooltip(Loc.Get(hint))
                                .Disabled(Busy)
                                .OnClick(() => SetDefaultEditor(editor, !isDefault))
                                .Show();

                            if (editor.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase)
                                && _releases.FirstOrDefault(release => release.Id == editor.ReleaseId) is {} installedRelease)
                            {
                                DrawChangelogButton(p, id + "notes", installedRelease, true);
                            }

                        }
                        Label(
                            p,
                            id + "meta",
                            Loc.Get("launcher.versions.installed_metadata", new
                            {
                                platform = editor.Platform, date = editor.InstalledAt.LocalDateTime.ToString("d")
                            }),
                            12,
                            Muted,
                            22
                        );
                    }

                    using (p.Row(id + "actions")
                        .Width(wide ? UnitValue.Auto : UnitValue.Stretch())
                        .MaxWidth(wide ? UnitValue.Pixels(ContentWidth(p) - 250) : UnitValue.Percentage(100))
                        .Height(UnitValue.Auto)
                        .WrapContent()
                        .Gap(8)
                        .LineGap(8)
                        .JustifyContent(LayoutJustification.End)
                        .Enter())
                    {
                        if (showMaintenance)
                        {
                            Button(p, id + "uninstall", "launcher.versions.uninstall", token => UninstallEditorAsync(editor, token), width: 110);
                            if (!editor.IsMain)
                            {
                                Button(p, id + "repair", "launcher.versions.repair",
                                    token => RepairEditorAsync(editor, token), width: 90);
                            }
                        }
                        Button(p, id + "launch", "launcher.versions.launch", token => LaunchAsync(editor, null, token), true, 90);
                    }
                }

            }
        }

        using (p.Row("available-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            Label(p, "available-title", "launcher.versions.available_title", 18, Ink, 35, true);

            using (p.Row("include-editor-prereleases-control").Width(UnitValue.Auto).Height(40)
                .Gap(8).AlignItems(LayoutAlignment.Center).Enter())
            {
                p.Box("include-editor-prereleases-label").Width(UnitValue.Auto).Height(24)
                    .Text(Loc.Get("launcher.versions.include_prereleases"), _font)
                    .FontSize(_appearance.Theme.Metrics.FontSize).TextColor(Ink)
                    .Alignment(TextAlignment.MiddleLeft)
                    .Cursor(Busy ? PaperCursor.Default : PaperCursor.Pointer)
                    .OnClick(_ =>
                    {
                        if (!Busy)
                        {
                            _includeEditorPrereleases = !_includeEditorPrereleases;
                        }
                    });
                Origami.Toggle(p, "include-editor-prereleases", _includeEditorPrereleases, value => _includeEditorPrereleases = value)
                    .NoLabel()
                    .Disabled(Busy).Show();
            }
            Button(p, "refresh", "launcher.versions.refresh", token => RefreshAsync(token), width: 96);
        }

        if (_includeEditorPrereleases)
        {
            using (VersionCard(p, "main"))
            {
                bool wide = IsWideLayout(p);
                using ((wide ? p.Row("main-row") : p.Column("main-row"))
                    .Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column("main-info").Height(UnitValue.Auto).Gap(2).Enter())
                    {
                        Label(p, "main-title", "launcher.versions.main_title", 18, Ink, 32, true);
                        p.Box("main-description")
                            .Height(UnitValue.Auto)
                            .IsNotInteractable()
                            .Text(Loc.Get("launcher.versions.main_description"), _font)
                            .FontSize(16)
                            .TextColor(Muted)
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

        HashSet<long> installedReleaseIds = _installed
            .Where(e => e.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ReleaseId).ToHashSet();
        EditorRelease[] available = _releases.Where(r => !installedReleaseIds.Contains(r.Id) && r.AssetFor(Platform.Identifier) != null && (_includeEditorPrereleases || !r.Preview))
            .ToArray();
        if (available.Length == 0)
        {
            Label(p, "no-releases", "launcher.versions.none_available", 14, Muted, 35);
        }

        foreach (EditorRelease release in available)
        {
            string id = "release-" + release.Id;
            using (VersionCard(p, id))
            {
                using ((IsWideLayout(p) ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
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
                                .Text(release.Tag, _bold).FontSize(19.8f)
                                .TextColor(Ink).Alignment(TextAlignment.MiddleLeft);
                            Color tagColor = release.Preview ? _appearance.Theme.Amber.C500 : Muted;
                            p.Box(id + "channel")
                                .Width(UnitValue.Auto).Height(24).Padding(8, 0)
                                .Rounded(_appearance.Theme.Metrics.Rounding)
                                .BackgroundColor(OrigamiTheme.WithAlpha(tagColor, 24))
                                .IsNotInteractable()
                                .Text(Loc.Get(release.Preview ? "launcher.versions.preview" : "launcher.versions.stable"), _font)
                                .FontSize(14).TextColor(tagColor)
                                .Alignment(TextAlignment.MiddleCenter);
                        }
                        Label(
                            p,
                            id + "meta",
                            $"{release.Published.LocalDateTime:d} · {release.AssetFor(Platform.Identifier)!.Size / 1048576d:F0} MB",
                            12,
                            Muted,
                            22
                        );
                    }

                    DrawChangelogButton(p, id + "notes", release);
                    Button(p, id + "install", "launcher.versions.install", token => InstallAsync(release, token), true, 90);
                }
            }
        }

    }

    private void DrawSourceBuildButton(Paper p, string id, string? repository = null, bool primary = false)
    {
        const string label = "launcher.versions.main_build";
        string text = Loc.Get(label);
        float width = Math.Max(154, text.Length * _appearance.Theme.Metrics.FontSize * 0.7f + 48);
        Origami.Button(p, id, text)
            .Width(width)
            .Height(40)
            .Rounding(_appearance.Theme.Metrics.Rounding)
            .LeadingIcon(LauncherIcons.ForAction(label))
            .Variant(primary ? OrigamiVariant.Primary : OrigamiVariant.Default)
            .Disabled(Busy)
            .OnClick(() => Start(token => BuildMainAsync(token, repository), label))
            .Show();
    }
}
