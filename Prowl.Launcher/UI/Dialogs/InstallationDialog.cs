using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private bool _showInstallationPrompt;
    private bool _createDesktopShortcut = true;

    private void ShowInstallationDialog()
    {
        Origami.Modal(Loc.Get("launcher.installation.title"))
            .Width(Math.Min(520, _window.FramebufferSize.X / DisplayScale - 48))
            .Content(p =>
            {
                using (p.Column("installation-options").Gap(18).Height(UnitValue.Auto).Enter())
                {
                    p.Box("installation-description")
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.installation.description"), _font)
                        .FontSize(18).TextColor(Ink).Wrap(TextWrapMode.Wrap).IsNotInteractable();
                    Origami.Checkbox(p, "desktop-shortcut", _createDesktopShortcut, value => _createDesktopShortcut = value)
                        .Label(Loc.Get("launcher.installation.desktop_shortcut"))
                        .Stretch().Show();
                }
            })
            .Button(Loc.Get("launcher.installation.keep_portable"), () =>
            {
                Modal.Pop();
                if (!installationPreview)
                {
                    store.Settings.InstallationPromptHandled = true;
                    store.Save();
                }
            })
            .Button(Loc.Get("launcher.installation.install"), () =>
            {
                Modal.Pop();
                if (!installationPreview)
                    Start(InstallLauncherAsync, "launcher.installation.installing");
            }, OrigamiVariant.Primary)
            .Show();
    }

    private async Task InstallLauncherAsync(CancellationToken token)
    {
        _restart = await new LauncherInstallationService(store).InstallAsync(_createDesktopShortcut, token);
    }
}
