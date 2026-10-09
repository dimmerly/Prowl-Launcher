using Prowl.Rosetta;

using System.Diagnostics;
using System.Security.Cryptography;

namespace Prowl.Launcher;

public static class PackageDownloadService
{
    public static async Task DownloadVerifiedAsync(
        HttpClient http,
        ReleaseAsset asset,
        string archive,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default
    )
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? download) || download.Scheme != "https" || download.Host != "github.com")
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        }

        if (asset.Digest == null || !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || asset.Digest.Length != 71)
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
            await using FileStream output = new( archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, Constants.Network.TransferBufferSize, true );
            byte[] buffer = new byte[Constants.Network.TransferBufferSize];
            long received = 0;
            Stopwatch clock = Stopwatch.StartNew();
            long lastReport = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                received += count;
                if (clock.ElapsedMilliseconds - lastReport > Constants.Network.ProgressIntervalMilliseconds)
                {
                    string transferred = size > 0 ? Loc.Get("launcher.download.transferred", new
                        {
                            received = (received / 1048576d).ToString("F1"), total = (size / 1048576d).ToString("F1")
                        }) : $"{received / 1048576d:F1} MB";
                    progress?.Report(new TransferProgress(Loc.Get("launcher.download.progress", new
                    {
                        name = asset.Name, progress = transferred
                    }), size > 0 ? (double)received / size : null));
                    lastReport = clock.ElapsedMilliseconds;
                }
            }

            if (asset.Size > 0 && received != asset.Size)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.incomplete_download"));
            }
        }

        progress?.Report(new TransferProgress("launcher.download.verifying"));
        await using (FileStream file = File.OpenRead(archive))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.digest_mismatch"));
            }
        }
    }
}
