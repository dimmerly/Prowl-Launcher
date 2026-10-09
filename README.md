# Prowl Launcher

The standalone Prowl project, editor-version, and sample launcher. The UI uses Paper and Origami with OpenTK; it does not reference the engine.

## Build

Install the .NET 10 SDK, then run:

```sh
dotnet run --project Prowl.Launcher/Prowl.Launcher.csproj
```

Windows, Linux, and macOS release packages include .NET. End users do not need an SDK.

## Install

Download the file for your operating system:

- Windows: `.exe` — open the downloaded launcher.
- Linux: `.AppImage` (x64 or ARM64) — allow the file to run as a program, then open it.
- macOS: `.dmg` (Intel or Apple Silicon) — open the disk image, then open **Prowl Launcher**.

Release filenames use `Prowl-Launcher-<version>-<platform>`, such as `Prowl-Launcher-1.0.0-preview-3-win-x64.exe`. GitHub displays friendly labels such as **Prowl Launcher for Windows (x64) .exe** for one-click downloads. Update ZIPs retain their filenames as display text.

On first launch, choose **Install** to copy the app into your user applications folder and optionally create a desktop shortcut. No administrator access is required. The launcher restarts from its installed copy; the original download stays intact. Windows also gets a Start menu entry, and Linux gets an application menu entry. **Keep portable** skips installation and remembers your choice. Source builds and screenshot captures skip this prompt.

Windows uses `%LOCALAPPDATA%/Programs/Prowl Launcher`, Linux uses the launcher's data folder under `Application`, and macOS uses `~/Applications/Prowl Launcher.app`. The DMG also provides the usual Applications shortcut for manual installation.

Windows downloads are unsigned. macOS apps use local ad-hoc signatures, without a Developer ID certificate or notarization. These do not establish publisher trust: Windows may show SmartScreen, and macOS may require allowing the app in Privacy & Security. AppImage support depends on the Linux desktop and its FUSE support.

Release ZIPs are kept for automatic updates. They contain separate application files with .NET bundled, rather than the single-file download. They are not needed for the initial install.

## Versions and releases

`VERSION.txt` is the launcher version. Bump it on `main` to publish a new version. Only pushes changing `VERSION.txt` trigger the release workflow; pull requests still build, and manual runs remain available. The workflow checks for an existing release before building and skips published versions. CI builds Windows x64, Linux x64/ARM64, and macOS x64/ARM64 packages, then creates the corresponding `v<version>` tag and GitHub release. Existing releases and tags are never replaced. Prerelease versions create prereleases.

The **Build and test** workflow builds the launcher and runs its tests on Windows, Linux, and macOS on pushes to `main` and pull requests. The release workflow requires this same test matrix to pass, tests embedded sample extraction on each release platform, and checks that the packaged download can be copied into an installation, render a window, and start a verified update before uploading its artifacts. Linux checks use Xvfb; Windows checks use a pinned software OpenGL fixture that is never included in downloads. The test workflow can also be run manually and does not build sample bundles or publish releases. Tests remain available locally:

In Preferences, opt in to launcher prereleases to receive preview updates. Turning this off while running a prerelease offers the latest stable release, including an older version, with confirmation before installation. The GitHub repository settings let you choose separate sources for editor releases and launcher updates.

```sh
dotnet test Prowl.Launcher.Test/Prowl.Launcher.Test.csproj -c Release
```

## Samples

Samples run in a separate self-contained host. The launcher UI remains independent of the engine; only sample packaging uses Prowl source. `EngineRevision.txt` pins the engine commit used by release and thumbnail workflows.

For local sample builds, check out `ProwlEngine/Prowl` into `Engine` at that revision, then run:

```sh
dotnet msbuild Samples/Samples.proj -t:Build -p:Configuration=Release -p:SampleOutputRoot=./artifacts/samples/
dotnet run --project Prowl.Launcher/Prowl.Launcher.csproj -p:SampleBundleDirectory=./artifacts/samples/Bundles
```

Use an absolute `SampleBundleDirectory` if invoking MSBuild from a different working directory. An ordinary build can run without an engine checkout; sample cards appear when sample bundles are embedded.

To include sample extraction in the local tests after building bundles:

```sh
dotnet test Prowl.Launcher.Test/Prowl.Launcher.Test.csproj -c Release -p:SampleBundleDirectory=./artifacts/samples/Bundles
```

The **Generate sample thumbnails** workflow is manual. It updates checked-in images only when at least 25% of their pixels visibly change, ignoring minor color noise. New samples receive an image on their first run. It can also run locally:

```sh
dotnet run --file .github/scripts/generate-sample-thumbnails.cs -- --engine ./Engine
```

## Updates

Launcher updates default to `dimmerly/Prowl-Launcher`, independently of the configurable editor repository. Downloads require HTTPS, the configured launcher repository, matching sizes, and GitHub's SHA-256 digest. Updates skip drafts and select newer versions; prereleases require opt-in, and opting out from a preview offers the latest stable version with confirmation.

Downloaded updates remain inactive until the new launcher renders its first frame and acknowledges startup. Failed or timed-out startup keeps the current launcher available. If a previously selected update fails on a later launch, the original entry point clears the target and opens its own window.

Settings saves merge each instance's changes under a shared file lock. Editor installations record their source repository, keeping custom repositories separate even when tags match. Existing installations without repository metadata retain their historical keys and are treated as coming from `ProwlEngine/Prowl`. Interrupted editor repairs retain a journal and backup; startup restores the previous installation when the replacement was not committed, and keeps backups if recovery cannot finish.

These checks trust the release publisher; they do not provide independent cryptographic signing of updates.
