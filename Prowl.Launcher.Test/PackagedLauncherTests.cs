using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace Prowl.Launcher.Test;

public sealed class PackagedLauncherTests
{
    [PackagedFact]
    [Trait("Category", "Packaged")]
    public async Task DownloadInstallsStartsAndUpdatesUsingThePackagedFiles()
    {
        string source = Path.GetFullPath(Environment.GetEnvironmentVariable("PROWL_LAUNCHER_PACKAGE")!);
        string archive = Path.GetFullPath(Environment.GetEnvironmentVariable("PROWL_LAUNCHER_UPDATE_ARCHIVE")!);
        Assert.True(File.Exists(source) || Directory.Exists(source), "The downloadable package is missing.");
        Assert.True(File.Exists(archive), "The update archive is missing.");
        string home = Path.Combine(Path.GetTempPath(), "ProwlPackageTests", Guid.NewGuid().ToString("N"));
        try
        {
            bool bundle = source.EndsWith(".app", StringComparison.Ordinal);
            string installed = Path.Combine(home, "Application", Path.GetFileName(source));
            LauncherInstallationService.CopyApplication(source, installed, bundle, default);
            Assert.True(LauncherInstallationService.SameApplication(source, installed, bundle, default));
            string executable = bundle ? Path.Combine(installed, "Contents", "MacOS", "Prowl.Launcher") : installed;
            await AssertStartsAndRenders(executable, home);

            byte[] bytes = await File.ReadAllBytesAsync(archive);
            ReleaseAsset asset = new(Path.GetFileName(archive),
                "https://github.com/dimmerly/Prowl-Launcher/releases/download/smoke/launcher.zip",
                bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)));
            EditorRelease release = new(1, "smoke", false, false, DateTimeOffset.UtcNow, "", null, [asset]);
            LauncherStore store = new(home);
            store.Settings.LauncherPrereleases = true;
            using HttpClient http = new(new ArchiveHandler(bytes));
            string updated = await new LauncherUpdaterService(http, store).InstallAsync(release, Platform.Identifier);
            Assert.Null(new LauncherStore(home).Settings.LauncherExecutable);
            await AssertStartsAndRenders(updated, home);
            store.Settings.LauncherExecutable = updated;
            store.Save();
            Assert.Equal(updated, new LauncherStore(home).Settings.LauncherExecutable);
        }
        finally
        {
            // Windows can briefly keep mapped DLLs locked after process termination.
            for (int attempt = 0; Directory.Exists(home); attempt++)
            {
                try { Directory.Delete(home, true); }
                catch (Exception e) when (attempt < 50 && e is IOException or UnauthorizedAccessException)
                { await Task.Delay(100); }
            }
        }
    }

    private static async Task AssertStartsAndRenders(string executable, string home)
    {
        // Hosted Windows runners use software OpenGL. These test fixtures are never packaged.
        string? graphics = Environment.GetEnvironmentVariable("PROWL_SMOKE_OPENGL_DIRECTORY");
        if (graphics != null)
        {
            Assert.True(File.Exists(Path.Combine(graphics, "opengl32.dll")), "The software OpenGL fixture is missing.");
            foreach (string library in Directory.GetFiles(graphics, "*.dll"))
                File.Copy(library, Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileName(library)), true);
        }
        int childId = 0;
        try
        {
            await LauncherStartupService.StartAsync(executable, home, ["--offline"],
                timeout: TimeSpan.FromSeconds(45), start: info =>
                {
                    info.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
                    Process process = Process.Start(info)!;
                    childId = process.Id;
                    return process;
                });
        }
        finally
        {
            if (childId != 0)
            {
                try
                {
                    using Process child = Process.GetProcessById(childId);
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
                catch (ArgumentException) { }
            }
        }
    }

    private sealed class ArchiveHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private sealed class PackagedFactAttribute : FactAttribute
    {
        public PackagedFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("PROWL_LAUNCHER_PACKAGE") == null)
                Skip = "Set PROWL_LAUNCHER_PACKAGE and PROWL_LAUNCHER_UPDATE_ARCHIVE to validate release packages.";
        }
    }
}
