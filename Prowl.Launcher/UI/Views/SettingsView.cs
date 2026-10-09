using Prowl.Rosetta;

using Prowl.Launcher.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private string? _prowlRepositoryDraft;
    private string? _launcherRepositoryDraft;
    private string _launcherStorageSize = "—";
    private Task? _storageSizeRefresh;
    private DateTimeOffset _nextStorageSizeRefresh;
    private void DrawRepositorySettings(Paper p)
    {
        using (Card(p, "repository-settings"))
        {
            Label(p, "repositories-title", "launcher.settings.repositories", 19, Ink, 28, true);
            Label(
                p,
                "repositories-info",
                "launcher.settings.repository_description",
                13,
                Muted,
                26
            );
            _prowlRepositoryDraft ??= store.Settings.ProwlRepository;
            Label(p, "prowl-repository-label", "launcher.settings.prowl_releases", 14, Ink, 26);
            Origami.TextField(p, "prowl-repository", _prowlRepositoryDraft, value => _prowlRepositoryDraft = value).Show();

            _launcherRepositoryDraft ??= store.Settings.LauncherRepository;
            Label(p, "launcher-repository-label", "launcher.settings.launcher_releases", 14, Ink, 26);
            Origami.TextField(p, "launcher-repository", _launcherRepositoryDraft, value => _launcherRepositoryDraft = value).Show();
            using (p.Row("repository-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                Button(p, "save-repositories", "launcher.settings.save_repositories", SaveRepositoriesAsync, true, 170);
                Button(p, "reset-repositories", "launcher.settings.official_repository", ResetRepositoryDraftsAsync, width: 165);
            }
        }
    }

    private void DrawSettings(Paper p)
    {
        if ((_storageSizeRefresh == null || _storageSizeRefresh.IsCompleted)
            && DateTimeOffset.UtcNow >= _nextStorageSizeRefresh)
        {
            _nextStorageSizeRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
            _storageSizeRefresh = RefreshStorageSizeAsync();
        }
        using (Card(p, "appearance-settings"))
        {
            Label(p, "appearance-title", "launcher.settings.appearance", 19, Ink, 28, true);
            Label(
                p,
                "appearance-info",
                "launcher.settings.appearance_description",
                13,
                Muted,
                26
            );
            using (p.Row("interface-size-row")
                .Height(42)
                .Gap(12)
                .Enter())
            {
                Label(p, "interface-size-label", "launcher.settings.interface_size", 14, Ink, 44);
                Origami.Dropdown(
                        p,
                        "interface-size",
                        (int)Math.Round(UiScale * 100),
                        value =>
                        {
                            store.Settings.UiScale = value / 100f;
                            store.Save();
                        },
                        new[]
                        {
                            50,
                            65,
                            75,
                            90,
                            100,
                            115,
                            125,
                            150
                        }
                    )
                    .Display(value => value + "%")
                    .Width(260)
                    .Show();
            }

            using (p.Row("theme-preset-row")
                .Height(42)
                .Gap(12)
                .Enter())
            {
                Label(p, "preset-label", "launcher.settings.theme", 14, Ink, 34);
                string[] names = ThemePresets.All.Select(preset => preset.Name).ToArray();
                Origami.Dropdown(p, "theme-preset", _appearance.Data.Name, name => _appearance.UsePreset(name), names)
                    .Width(260)
                    .Show();
            }

            ColorSetting(
                p,
                "accent-color",
                "launcher.settings.accent",
                _appearance.Data.Purple.Primary,
                value => CustomizeRamp(_appearance.Data.Purple, value)
            );
            ColorSetting(
                p,
                "logo-color",
                "launcher.settings.logo_color",
                store.Settings.LogoColor ?? ColorRamp.ColorToHex(Ink),
                value =>
                {
                    store.Settings.LogoColor = value;
                    store.Save();
                }
            );
            ColorSetting(
                p,
                "panel-color",
                "launcher.settings.panels",
                _appearance.Data.Neutral.Primary,
                value => CustomizeRamp(_appearance.Data.Neutral, value)
            );
            ColorSetting(
                p,
                "text-color",
                "launcher.settings.text",
                _appearance.Data.Ink.Primary,
                value => CustomizeRamp(_appearance.Data.Ink, value)
            );
            ColorSetting(
                p,
                "background-color",
                "launcher.settings.background",
                ColorRamp.ColorToHex(_appearance.Background),
                value =>
                {
                    _appearance.Data.BackgroundStyle = EditorBackgroundStyle.Color;
                    _appearance.Data.BackgroundColorA = value;
                    _appearance.Save();
                }
            );
            using (p.Row("appearance-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                Origami.Button(p, "use-editor-theme", Loc.Get("launcher.settings.use_editor_theme"))
                    .Width(190)
                    .Height(40)
                    .LeadingIcon(OrigamiIconSet.Pencil)
                    .OnClick(_appearance.UseEditorTheme)
                    .Show();
                Origami.Button(p, "reset-theme", Loc.Get("launcher.settings.reset_to_default"))
                    .Width(190)
                    .Height(40)
                    .LeadingIcon(LauncherIcons.Refresh)
                    .OnClick(() => _appearance.UsePreset(ThemePresets.Default.Name))
                    .Show();
            }
        }

        using (Card(p, "launch-settings"))
        {
            Label(p, "launch-settings-title", "launcher.settings.title", 19, Ink, 28, true);
            Origami.Switch(p, "close-on-editor-launch", store.Settings.CloseOnEditorLaunch, value =>
                {
                    store.Settings.CloseOnEditorLaunch = value;
                    store.Save();
                })
                .LabelLeft(Loc.Get("launcher.settings.close_on_launch"))
                .Stretch()
                .Show();
            Origami.Switch(p, "show-fps", store.Settings.ShowFps, value =>
                {
                    store.Settings.ShowFps = value;
                    store.Save();
                })
                .LabelLeft(Loc.Get("launcher.settings.show_fps"))
                .Stretch()
                .Show();
        }

        using (Card(p, "launcher-settings"))
        {
            using (p.Row("launcher-storage-heading").Height(28).Enter())
            {
                Label(p, "launcher-settings-title", "launcher.settings.updates_and_storage", 19, Ink, 28, true);
                p.Box("launcher-file-size")
                    .Width(110)
                    .Height(28)
                    .IsNotInteractable()
                    .Text(_launcherStorageSize, _font)
                    .FontSize(16)
                    .TextColor(Muted)
                    .Alignment(TextAlignment.MiddleRight);
            }
            Label(p, "launcher-version", Loc.Get("launcher.settings.version", new
            {
                version = LauncherVersion,
                platform = Platform.Identifier
            }), 13, Muted, 24);

            Origami.Toggle(p, "launcher-prereleases", store.Settings.LauncherPrereleases, value =>
                {
                    store.Settings.LauncherPrereleases = value;
                    store.Save();
                    if (value)
                        _ = CheckLauncherInBackgroundAsync();
                    else if (LauncherVersion.Split('+')[0].Contains('-'))
                        Start(UpdateLauncherAsync, "launcher.updates.checking");
                })
                .LabelLeft(Loc.Get("launcher.settings.launcher_prereleases"))
                .Stretch().Disabled(Busy).Show();

            using (p.Row("launcher-settings-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                Button(p, "launcher-update", "launcher.updates.check", UpdateLauncherAsync, width: 160);
                Button(
                    p,
                    "data-folder",
                    "launcher.settings.open_data_folder",
                    _ =>
                    {
                        Open(store.Home);
                        return Task.CompletedTask;
                    },
                    width: 155
                );
            }
        }

        DrawRepositorySettings(p);
    }

    private void CustomizeRamp(ColorRamp ramp, string value)
    {
        ramp.Primary = value;
        ramp.OverrideAll = false;
        _appearance.Save();
    }

    private void ColorSetting(Paper p, string id, string label, string hex, Action<string> changed)
    {
        using (p.Row(id + "-row")
            .Height(42)
            .Gap(12)
            .Enter())
        {
            Label(p, id + "-label", label, 14, Ink, 34);
            Color color = ColorRamp.ParseHex(hex);
            Origami.ColorField(
                    p,
                    id,
                    new Prowl.Vector.Color(color.R / 255f, color.G / 255f, color.B / 255f, 1),
                    value => changed(ColorRamp.ColorToHex(Color.FromArgb(
                        255,
                        (int)(Math.Clamp(value.R, 0, 1) * 255),
                        (int)(Math.Clamp(value.G, 0, 1) * 255),
                        (int)(Math.Clamp(value.B, 0, 1) * 255)
                    )))
                )
                .Width(260)
                .Height(40)
                .Show();
        }
    }
}
