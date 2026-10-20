using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using Prowl.Rosetta;

namespace Prowl.Launcher;

/// <summary>Validates, downloads and unpacks GitHub release assets (editors, launcher updates and samples).</summary>
internal static class ReleasePackage
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

    /// <summary>
    ///     Downloads <paramref name="asset" /> to <paramref name="archive" /> and verifies its SHA-256 digest. With a
    ///     <paramref name="partialDirectory" />, an interrupted or cancelled download is kept there (named by its
    ///     digest) and the next attempt resumes it with a range request.
    /// </summary>
    public static async Task DownloadVerifiedAsync(
        HttpClient http,
        ReleaseAsset asset,
        string archive,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default,
        string? partialDirectory = null
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

        string digest = asset.Digest![DigestPrefix.Length..].ToLowerInvariant();
        string partial = archive;
        if (partialDirectory != null)
        {
            Directory.CreateDirectory(partialDirectory);
            partial = Path.Combine(partialDirectory, digest + ".part");
            RemoveStalePartials(partialDirectory, partial);
        }

        long existing = partialDirectory != null && File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (asset.Size > 0 && existing > asset.Size)
        {
            File.Delete(partial);
            existing = 0;
        }

        progress?.Report(new TransferProgress(Loc.Get("launcher.download.starting", new { name = asset.Name }),
            asset.Size > 0 ? (double)existing / asset.Size : 0));
        if (asset.Size <= 0 || existing < asset.Size)
        {
            RequireFreeSpace(partial, asset.Size - existing);
            await DownloadAsync(http, download, asset, partial, existing, progress, token);
        }

        progress?.Report(new TransferProgress("launcher.download.verifying"));
        string hash;
        await using (FileStream file = File.OpenRead(partial))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        }

        if (!hash.Equals(digest, StringComparison.OrdinalIgnoreCase))
        {
            // Don't resume from bytes that can't be trusted.
            File.Delete(partial);
            throw new InvalidDataException(Loc.Get("launcher.errors.digest_mismatch"));
        }

        if (partial != archive)
        {
            File.Move(partial, archive);
        }
    }

    private static async Task DownloadAsync(HttpClient http, Uri download, ReleaseAsset asset, string path,
        long existing,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, download);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using HttpResponseMessage response =
            await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The kept bytes no longer match the server's file; start over.
            File.Delete(path);
            await DownloadAsync(http, download, asset, path, 0, progress, token);
            return;
        }

        response.EnsureSuccessStatusCode();

        // A server that ignores the range sends the whole file again.
        bool resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        long offset = resumed ? existing : 0;
        long size = response.Content.Headers.ContentLength is { } length ? offset + length : asset.Size;
        await using Stream input = await response.Content.ReadAsStreamAsync(token);
        await using FileStream output = new(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None,
            Constants.Network.TransferBufferSize, true);
        long received = await CopyWithProgressAsync(input, output, asset.Name, offset, size, progress, token);
        if (asset.Size > 0 && received != asset.Size)
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.incomplete_download"));
        }
    }

    /// <summary>
    ///     Throws when the drive holding <paramref name="path" /> has less than <paramref name="bytes" /> free, plus a
    ///     small margin. Drives whose free space can't be read are allowed; writing then fails as it normally would.
    /// </summary>
    internal static void RequireFreeSpace(string path, long bytes)
    {
        const long Margin = 64L * 1024 * 1024;
        if (bytes <= 0 || FreeSpace(path) is not { } available)
        {
            return;
        }

        long needed = bytes > long.MaxValue - Margin ? long.MaxValue : bytes + Margin;
        if (available < needed)
        {
            throw new IOException(Loc.Get("launcher.errors.not_enough_space",
                new { needed = Gigabytes(needed), available = Gigabytes(available) }));
        }
    }

    /// <summary>Free bytes on the drive or mount holding <paramref name="path" />, or null when unknown.</summary>
    private static long? FreeSpace(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            DriveInfo? drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && FileSystemHelper.IsInside(d.RootDirectory.FullName, full))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Drops partial downloads that nothing resumed within two weeks.</summary>
    private static void RemoveStalePartials(string directory, string keep)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.part"))
        {
            try
            {
                if (file != keep && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // In use by another launcher; leave it.
            }
        }
    }

    private static string Gigabytes(long bytes) => (bytes / 1073741824d).ToString("F1");

    /// <summary>
    ///     Extracts a zip into <paramref name="staging" />, rejecting path traversal, links, special files and
    ///     archives that expand beyond <paramref name="maxExpandedBytes" />. Unix modes are preserved.
    /// </summary>
    public static void ExtractArchive(string archive, string staging, CancellationToken token = default,
        long maxExpandedBytes = Constants.Storage.MaxExpandedArchiveBytes)
    {
        const int UnixFileTypeMask = 0xf000, UnixRegularFile = 0x8000, UnixDirectory = 0x4000;

        Directory.CreateDirectory(staging);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        long declared = zip.Entries.Aggregate(0L, (total, entry) => total > maxExpandedBytes
            ? total
            : total + entry.Length);
        if (declared <= maxExpandedBytes)
        {
            RequireFreeSpace(staging, declared);
        }

        long expanded = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            string path = FileSystemHelper.SafeChildPath(staging, relative);
            int unixMode = (entry.ExternalAttributes >> 16) & 0xffff;
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
            if (expanded > maxExpandedBytes)
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
            using (FileStream output = new(path, FileMode.CreateNew))
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
        Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
        uri.Host == "github.com";

    private static async Task<long> CopyWithProgressAsync(Stream input, Stream output, string name, long offset,
        long size,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[Constants.Network.TransferBufferSize];
        Stopwatch clock = Stopwatch.StartNew();
        long received = offset, lastReport = 0;
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
                ? Loc.Get("launcher.download.transferred",
                    new { received = Megabytes(received), total = Megabytes(size) })
                : $"{Megabytes(received)} MB";
            double? fraction = size > 0 ? (double)received / size : null;
            progress?.Report(
                new TransferProgress(Loc.Get("launcher.download.progress", new { name, progress = transferred }),
                    fraction));
            lastReport = clock.ElapsedMilliseconds;
        }

        return received;
    }

    private static string Megabytes(long bytes) => (bytes / 1048576d).ToString("F1");
}
