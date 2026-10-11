using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;

using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>Validates, downloads and unpacks GitHub release assets (editors, launcher updates and samples).</summary>
static class ReleasePackage
{
    private const string DigestPrefix = "sha256:";

    internal static bool HasValidDigest(ReleaseAsset asset) =>
        asset.Digest is { Length: 71 } digest && digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Rejects assets that are not release downloads of <paramref name="repository" /> (and, optionally, of
    ///     <paramref name="tag" />).
    /// </summary>
    internal static void RequireFromRepository(ReleaseAsset asset, string repository, string? tag = null)
    {
        string prefix = $"/{repository}/releases/download/" + (tag == null ? "" : tag + "/");
        if (!TryGetGitHubUri(asset, out Uri? source)
            || !source.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        }
    }

    public static async Task DownloadVerifiedAsync(
        HttpClient http,
        ReleaseAsset asset,
        string archive,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default
    )
    {
        if (!TryGetGitHubUri(asset, out Uri? download))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        }
        if (!HasValidDigest(asset))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.missing_digest"));
        }

        progress?.Report(new TransferProgress(Loc.Get("launcher.download.starting", new
        {
            name = asset.Name
        }), 0));
        using (HttpResponseMessage response = await http.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            long size = response.Content.Headers.ContentLength ?? asset.Size;
            await using Stream input = await response.Content.ReadAsStreamAsync(token);
            await using FileStream output = new( archive, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                Constants.Network.TransferBufferSize, true );
            long received = await CopyWithProgressAsync(input, output, asset.Name, size, progress, token);
            if (asset.Size > 0 && received != asset.Size)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.incomplete_download"));
            }
        }

        progress?.Report(new TransferProgress("launcher.download.verifying"));
        await using (FileStream file = File.OpenRead(archive))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(asset.Digest![DigestPrefix.Length..], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.digest_mismatch"));
            }
        }
    }

    /// <summary>
    ///     Extracts a zip into <paramref name="staging" />, rejecting path traversal, links, special files and
    ///     archives that expand beyond <see cref="Constants.Storage.MaxExpandedArchiveBytes" />. Unix modes are preserved.
    /// </summary>
    public static void ExtractArchive(string archive, string staging, CancellationToken token = default)
    {
        const int UnixFileTypeMask = 0xf000, UnixRegularFile = 0x8000, UnixDirectory = 0x4000;

        Directory.CreateDirectory(staging);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        long expanded = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            string path = FileSystemHelper.SafeChildPath(staging, relative);
            int unixMode = entry.ExternalAttributes >> 16 & 0xffff;
            int fileType = unixMode & UnixFileTypeMask;
            if (fileType is not (0 or UnixRegularFile or UnixDirectory))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.unsupported_archive_entry"));
            }
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.archive_link"));
            }

            expanded = checked(expanded + entry.Length);
            if (expanded > Constants.Storage.MaxExpandedArchiveBytes)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.archive_too_large"));
            }

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (Stream input = entry.Open())
            using (FileStream output = new( path, FileMode.CreateNew ))
            {
                input.CopyTo(output);
            }

            if (!OperatingSystem.IsWindows() && unixMode != 0)
            {
                File.SetUnixFileMode(path, (UnixFileMode)(unixMode & 0x1ff));
            }
        }
    }

    /// <summary>Adds the user execute bit on Unix; a no-op on Windows.</summary>
    internal static void MarkExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }
    }

    private static bool TryGetGitHubUri(ReleaseAsset asset, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "github.com";

    private static async Task<long> CopyWithProgressAsync(Stream input, Stream output, string name, long size,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[Constants.Network.TransferBufferSize];
        Stopwatch clock = Stopwatch.StartNew();
        long received = 0, lastReport = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            received += count;
            if (clock.ElapsedMilliseconds - lastReport <= Constants.Network.ProgressIntervalMilliseconds)
            {
                continue;
            }

            string transferred = size > 0
                ? Loc.Get("launcher.download.transferred", new
                {
                    received = Megabytes(received), total = Megabytes(size)
                })
                : $"{Megabytes(received)} MB";
            double? fraction = size > 0 ? (double)received / size : null;
            progress?.Report(new TransferProgress(Loc.Get("launcher.download.progress", new
            {
                name, progress = transferred
            }), fraction));
            lastReport = clock.ElapsedMilliseconds;
        }
        return received;
    }

    private static string Megabytes(long bytes) => (bytes / 1048576d).ToString("F1");
}
