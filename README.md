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
- Linux: `.AppImage` — allow the file to run as a program, then open it.
- macOS: `.dmg` — open the disk image, then open **Prowl Launcher**.

On first launch, choose **Install** to copy the app into your user applications folder and optionally create a desktop shortcut. No administrator access is required. The launcher restarts from its installed copy; the original download stays intact. Windows also gets a Start menu entry, and Linux gets an application menu entry. **Keep portable** skips installation and remembers your choice. Source builds and screenshot captures skip this prompt.

Windows uses `%LOCALAPPDATA%/Programs/Prowl Launcher`, Linux uses the launcher's data folder under `Application`, and macOS uses `~/Applications/Prowl Launcher.app`. The DMG also provides the usual Applications shortcut for manual installation.

Windows downloads are unsigned. macOS apps use local ad-hoc signatures, without a Developer ID certificate or notarization. These do not establish publisher trust: Windows may show SmartScreen, and macOS may require allowing the app in Privacy & Security. AppImage support depends on the Linux desktop and its FUSE support.

Release ZIPs are kept for automatic updates. They are not needed for the initial install.

## Versions and releases

`VERSION.txt` is the launcher version. Bump it on `main` to publish a new version. Only pushes changing `VERSION.txt` trigger the release workflow; pull requests still build, and manual runs remain available. The workflow checks for an existing release before building and skips published versions. CI builds Windows x64, Linux x64/ARM64, and macOS x64/ARM64 packages, then creates the corresponding `v<version>` tag and GitHub release. Existing releases and tags are never replaced. Prerelease versions create prereleases.

Launcher tests remain available locally; they are not run by CI/CD.

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

Launcher updates come from `TODO`, independently of the configurable editor repository. Downloads require HTTPS, the expected launcher repository, matching sizes, and GitHub's SHA-256 digest. Updates skip drafts, prerelease releases, and versions that are not newer.

These checks trust the release publisher; they do not provide independent cryptographic signing of updates.
