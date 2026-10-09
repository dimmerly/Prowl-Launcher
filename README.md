# Prowl Launcher

Install and manage Prowl editor versions, create and open projects, and try sample scenes.

## Install

Download the launcher from [GitHub Releases](https://github.com/dimmerly/Prowl-Launcher/releases):

- **Windows:** open the `.exe`.
- **Linux:** make the `.AppImage` executable, then open it.
- **macOS:** open the `.dmg`, then open **Prowl Launcher**.

On first launch, choose **Install** or **Keep portable**. The launcher includes .NET; no SDK is needed to run it.

Downloads are unsigned on Windows and not notarized on macOS, so your OS may ask you to allow the app.

## Updates

The launcher checks for updates at startup and shows the changelog before installing. Dismissed releases stay dismissed across restarts; **Check for updates** in Settings lets you revisit them.

Enable prereleases in Settings to check for preview updates immediately. Disable them to switch back to the latest stable release. Editor and launcher release repositories can also be changed in Settings.

## Development

Install the .NET 10 SDK, then run:

```sh
dotnet run --project src/Prowl.Launcher.csproj
```

Source lives in `src`. One test project contains `tests/Unit`, `tests/Integration`, and `tests/E2E`.

```sh
dotnet test tests/Prowl.Launcher.Test.csproj -c Release
```

E2E tests run in CI on Windows, Linux, and macOS and gate releases. They are excluded from the default local test command. To run them with a graphical display and OpenGL available:

```sh
dotnet test tests/Prowl.Launcher.Test.csproj -c Release --filter Category=E2E
```

On Linux, prefix this command with `xvfb-run -a`. Failed E2E tests save logs, a UI tree, and a screenshot in the temporary `ProwlLauncherE2E` folder.

### Samples

The first sample you open downloads all samples and their runtime in one bundle, with progress at the top. Later runs use the cached copy, including offline. **Update samples** appears when a newer bundle is available; updates download only when you click it.

Releases publish `Prowl-Samples-<platform>.zip` separately from the launcher. To build one locally, check out [ProwlEngine/Prowl](https://github.com/ProwlEngine/Prowl) into `Engine` at the commit in `EngineRevision.txt`, then run:

```sh
dotnet run --file .github/scripts/package-samples.cs -- Engine artifacts/samples win-x64 artifacts/Prowl-Samples-win-x64.zip
```

Replace `win-x64` with your target platform. The script generates and builds the sample host; an ordinary launcher build needs no engine checkout.

### Releases

Bump `VERSION.txt` on `main` to publish a release. CI runs tests on Windows, Linux, and macOS, builds the packages and sample bundles, and checks packaged startup and updates before publishing. Versions already published are skipped.
