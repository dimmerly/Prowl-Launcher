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

E2E tests are excluded by default and from CI. To run them with a graphical display and OpenGL available:

```sh
dotnet test tests/Prowl.Launcher.Test.csproj -c Release --filter Category=E2E
```

On Linux, prefix this command with `xvfb-run -a`. Failed E2E tests save logs, a UI tree, and a screenshot in the temporary `ProwlLauncherE2E` folder.

### Samples

To build sample bundles, check out [ProwlEngine/Prowl](https://github.com/ProwlEngine/Prowl) into `Engine` at the commit in `EngineRevision.txt`, then run:

```sh
dotnet msbuild Samples/Samples.proj -t:Build -p:Configuration=Release -p:SampleOutputRoot=./artifacts/samples/
dotnet run --project src/Prowl.Launcher.csproj -p:SampleBundleDirectory=./artifacts/samples/Bundles
```

An ordinary launcher build does not require an engine checkout.

### Releases

Bump `VERSION.txt` on `main` to publish a release. CI runs tests on Windows, Linux, and macOS, builds the packages and sample bundles, and checks packaged startup and updates before publishing. Versions already published are skipped.
