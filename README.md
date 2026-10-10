# Prowl Launcher

Install and manage Prowl editor versions, create and open projects, and try sample scenes.

## Install

Download the launcher from [GitHub Releases](https://github.com/dimmerly/Prowl-Launcher/releases):

- **Windows:** open the `.exe`.
- **Linux:** make the `.AppImage` executable, then open it.
- **macOS:** open the `.dmg`, then open **Prowl Launcher**.

On first launch, choose **Install** or **Keep portable**. The launcher includes .NET; no SDK is needed to run it.

Preview downloads are unsigned on Windows and not notarized on macOS unless preview signing is enabled, so your OS may
ask you to allow the app. Stable releases require signing and macOS notarization; maintainers must configure
the [release signing credentials](docs/release-signing.md).

## Updates

The launcher checks for updates at startup and shows the changelog before installing. **Automatically check for launcher
updates** in Settings controls these checks and is enabled by default. Manual **Check for updates** remains available
when automatic checks are disabled. Dismissed releases stay dismissed across restarts; **Check for updates** in Settings
lets you revisit them.

Enable prereleases in Settings to check for preview updates immediately. Disable them to switch back to the latest
stable release. Editor and launcher release repositories can also be changed in Settings.

## Latest editor from source

In **Editor Versions**, enable prereleases, then choose **Latest (main)** and click **Pull & build** to compile
the editor from the configured Prowl repository's `main` branch. Install Git and the
.NET 10 SDK first, and make sure both are on your `PATH`.

The launcher clones once, pulls changes with `git pull --ff-only`, updates submodules,
and builds in Release mode. Launch it or select it for a project like any other
installed editor. Each commit gets its own installation, identified by its commit ID;
older builds and project selections stay intact. **Latest (main)** remains available
for updates. If that commit is already installed, the launcher skips compilation.
A failed or cancelled build keeps all existing builds.

The checkout stays under `Source/<repository-hash>/` in the launcher data folder,
including after uninstalling the compiled editor. Commit or remove local changes
before pulling. Source builds can contain unfinished or broken changes.

## News


News appears below your projects, with arrows to browse three posts at a time.
The feed updates automatically, and previously opened posts and images work offline.

To add a post, create `news/<post-name>/README.md` with an `images/` folder beside it,
then add an entry to [`news/index.json`](news/index.json):

```json
{
  "file": "rendering-showcase/README.md",
  "title": "Rendering showcase",
  "date": "2026-10-10",
  "author": "Wulferis",
  "thumbnail": "images/thumbnail.png"
}
```

`file`, `title`, and `date` are required; `author`, `summary`, and `thumbnail` are optional.
Use letters, digits, underscores or hyphens in folder and file names. Dates use
`YYYY-MM-DD`; newest posts appear first. Keep archived posts at their original dates.

Use ordinary Markdown. Image paths are relative to the post, for example
`![Screenshot](images/screenshot.png)`. Thumbnails use the same paths; HTTPS image
URLs also work. Use HTTPS links for external pages and videos.

Commit the post, images, and index to `main` in the configured launcher repository
to publish; no launcher release is needed. Release builds check every fifteen minutes.
Debug builds (`dotnet run --project src`) read your local `news/` folder, including
offline. Index edits appear automatically; reopen a post after editing its text or images.

## Data folder

On Windows, launcher data lives in `%APPDATA%\Prowl\Launcher` (usually
`C:\Users\<user>\AppData\Roaming\Prowl\Launcher`). Editors install separately in `%APPDATA%\Prowl\Versions`; **Open
install folder** on the Versions page opens that location. Existing editors under `Launcher\Versions` are not migrated
or discovered. **Open data folder** in Settings opens the launcher data location currently in use. `PROWL_LAUNCHER_HOME`
overrides this location, including for portable runs; a custom home keeps editors in its own `Versions` folder.

Files and folders appear as the launcher needs them:

| Path                                                            | Purpose                                                                                                                                                                                                                                                                                             |
|-----------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `settings.json`                                                 | Preferences, project paths and editor pins, release repositories, dismissed updates, and the active launcher executable. Projects themselves stay in their chosen folders.                                                                                                                          |
| `settings.json.bak`                                             | A copy of the last saved settings, used when the main file cannot be read.                                                                                                                                                                                                                          |
| `settings.json.corrupt-<id>` / `settings.json.bak.corrupt-<id>` | Damaged settings preserved during recovery.                                                                                                                                                                                                                                                         |
| `theme.json`                                                    | Launcher theme, colors, and roundness.                                                                                                                                                                                                                                                              |
| `releases.json`                                                 | The current editor repository's release list for offline use. Launcher update checks use the network.                                                                                                                                                                                               |
| `News/<repository>/`                                            | Cached news index and previously opened Markdown articles for offline reading.                                                                                                                                                                                                                      |
| `Updates/<repository-hash>/<version>-<platform>/`               | Downloaded launcher updates. Each contains the app and `launcher-installation.json`, which records its download digest, executable, and file hashes. Cleanup keeps the selected update, running copies, and original installed launcher; unused copies are removed after activation and at startup. |
| `Samples/`                                                      | Downloaded sample bundle. Updates replace its contents. `installed.json` records its source release and digest. `content/samples.json` lists sample IDs; `content/Host/` contains their shared runtime, and `content/Samples/<id>/` contains each sample's assembly and assets.                     |
| `Work/<id>/`                                                    | Temporary editor or launcher downloads and extraction. May contain `editor.zip` or `launcher.zip`, `extracted/`, and an editor repair's `repair.json` and `previous/` backup. Completed work is removed; interrupted repairs retain what is needed for recovery.                                    |
| `Work/samples-download-<id>/`                                   | Temporary sample download, including `samples.zip`, extracted `content/`, and `installed.json`.                                                                                                                                                                                                     |
| `Work/sample-<id>/`                                             | A running sample's copy of its runtime and assets, plus `stdout.log` and `stderr.log`. Successful runs remove it; failed runs retain it for diagnosis.                                                                                                                                              |
| `settings.lock`                                                 | Prevents simultaneous settings writes from different launcher instances.                                                                                                                                                                                                                            |
| `operations.lock`                                               | Coordinates editor installation, launcher installation, repair, and cleanup.                                                                                                                                                                                                                        |
| `launcher-update.lock`                                          | Protects the launcher download, startup check, and activation from cleanup by another instance.                                                                                                                                                                                                     |
| `samples.lock`                                                  | Coordinates sample downloads and recovery. Lock files can remain when no operation is running.                                                                                                                                                                                                      |
| `launcher.log`                                                  | Operation errors, recovery messages, and launcher update fallback errors.                                                                                                                                                                                                                           |
| `startup-error.log`                                             | Errors that prevent launcher startup.                                                                                                                                                                                                                                                               |
| `*.previous`                                                    | Temporary rollback folders beside launcher updates or sample bundles during replacement and recovery.                                                                                                                                                                                               |
| `*.tmp`                                                         | Temporary files used to write JSON before replacing the destination file.                                                                                                                                                                                                                           |

Each editor installation in `Versions/` has an `installation.json` recording its repository, release, platform,
executable, and installation date. These versions remain until uninstalled. Repository hashes distinguish downloads from
different repositories; they are not Git commit IDs. Version folders also distinguish platforms such as `win-x64` and
`win-arm64`.

The editor keeps `EditorSettings.json` and `RecentProjects.json` one level up, in `%APPDATA%\Prowl`. The launcher reads
editor settings for its initial language, theme, and project location, and imports recent projects once. The Windows
launcher installed through **Install** lives at `%APPDATA%\Prowl\Launcher\Prowl.Launcher.exe`, which is also the target
of its Desktop and Start menu shortcuts.

## Development

Install the .NET 10 SDK, then run:

```sh
dotnet run --project src/Prowl.Launcher.csproj
```

Source lives in `src`. One test project contains `tests/Unit`, `tests/Integration`, and `tests/E2E`.

```sh
dotnet test tests/Prowl.Launcher.Test.csproj -c Release
```

E2E tests run in CI on Windows, Linux, and macOS and gate releases. They are excluded from the default local test
command. To run them with a graphical display and OpenGL available:

```sh
dotnet test tests/Prowl.Launcher.Test.csproj -c Release --filter Category=E2E
```

On Linux, prefix this command with `xvfb-run -a`. Failed E2E tests save logs, a UI tree, and a screenshot in the
temporary `ProwlLauncherE2E` folder.

### Samples

The first sample you open downloads all samples and their runtime in one bundle, with progress at the top. Later runs
use the cached copy, including offline. **Update samples** appears when a newer bundle is available; updates download
only when you click it.

Releases publish `Prowl-Samples-<platform>.zip` separately from the launcher. To build one locally, check
out [ProwlEngine/Prowl](https://github.com/ProwlEngine/Prowl) into `Engine` at the commit in `EngineRevision.txt`, then
run:

```sh
dotnet run --file .github/scripts/package-samples.cs -- Engine artifacts/samples win-x64 artifacts/Prowl-Samples-win-x64.zip
```

Replace `win-x64` with your target platform. The script generates and builds the sample host; an ordinary launcher build
needs no engine checkout.

### Releases

Bump `VERSION.txt` on `main` to publish a release. CI runs tests on Windows, Linux, and macOS, builds the packages and
sample bundles, and checks packaged startup and updates before publishing. Versions already published are skipped.
