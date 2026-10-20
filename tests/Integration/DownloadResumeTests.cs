using System.Net;
using System.Security.Cryptography;

using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class DownloadResumeTests : IDisposable
{
    private readonly byte[] _file = RandomNumberGenerator.GetBytes(256 * 1024);

    private readonly string _home =
        Path.Combine(Path.GetTempPath(), "ProwlDownloadTests", Guid.NewGuid().ToString("N"));

    private string Partials => Path.Combine(_home, "Downloads");

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }

    private ReleaseAsset Asset(byte[]? digestOf = null) => new("editor.zip",
        "https://github.com/ProwlEngine/Prowl/releases/download/v1.0.0/editor.zip", _file.Length,
        "sha256:" + Convert.ToHexString(SHA256.HashData(digestOf ?? _file)));

    private string Archive(string name = "editor.zip")
    {
        string work = Path.Combine(_home, "Work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return Path.Combine(work, name);
    }

    [Fact]
    public async Task AnInterruptedDownloadResumesWhereItStopped()
    {
        List<long?> ranges = [];
        bool drop = true;
        using HttpClient http = new(new Handler(request =>
        {
            long from = request.Headers.Range?.Ranges.Single().From ?? 0;
            ranges.Add(request.Headers.Range?.Ranges.Single().From);
            if (drop)
            {
                drop = false;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new DroppingStream(_file, 100_000))
                };
            }

            HttpResponseMessage response = new(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(_file[(int)from..])
            };
            return response;
        }));

        await Assert.ThrowsAsync<IOException>(() =>
            ReleasePackage.DownloadVerifiedAsync(http, Asset(), Archive(), partialDirectory: Partials));
        Assert.Equal(100_000, new FileInfo(Assert.Single(Directory.GetFiles(Partials, "*.part"))).Length);

        string archive = Archive();
        await ReleasePackage.DownloadVerifiedAsync(http, Asset(), archive, partialDirectory: Partials);
        Assert.Equal(_file, await File.ReadAllBytesAsync(archive));
        Assert.Equal([null, 100_000], ranges);
        Assert.Empty(Directory.GetFiles(Partials));
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeRestartsTheDownload()
    {
        Directory.CreateDirectory(Partials);
        string digest = Convert.ToHexString(SHA256.HashData(_file)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Path.Combine(Partials, digest + ".part"), _file[..1000]);
        using HttpClient http = new(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_file)
        }));

        string archive = Archive();
        await ReleasePackage.DownloadVerifiedAsync(http, Asset(), archive, partialDirectory: Partials);
        Assert.Equal(_file, await File.ReadAllBytesAsync(archive));
    }

    [Fact]
    public async Task ACorruptResumedDownloadIsDiscarded()
    {
        Directory.CreateDirectory(Partials);
        string digest = Convert.ToHexString(SHA256.HashData(_file)).ToLowerInvariant();
        byte[] stale = RandomNumberGenerator.GetBytes(1000);
        await File.WriteAllBytesAsync(Path.Combine(Partials, digest + ".part"), stale);
        using HttpClient http = new(new Handler(request => new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(_file[(int)request.Headers.Range!.Ranges.Single().From!.Value..])
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReleasePackage.DownloadVerifiedAsync(http, Asset(), Archive(), partialDirectory: Partials));
        Assert.Empty(Directory.GetFiles(Partials));
    }

    [Fact]
    public async Task AStalledDownloadFailsAndKeepsItsProgress()
    {
        using CancellationTokenSource cleanup = new();
        using HttpClient http = new(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_file, 100_000))
        }));

        Task download = ReleasePackage.DownloadVerifiedAsync(http, Asset(), Archive(), token: cleanup.Token,
            partialDirectory: Partials, idleTimeout: TimeSpan.FromMilliseconds(200));
        try
        {
            await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(download.IsCompleted, "The download is still waiting for data.");
            await Assert.ThrowsAsync<TimeoutException>(() => download);
            Assert.Equal(100_000, new FileInfo(Assert.Single(Directory.GetFiles(Partials, "*.part"))).Length);
        }
        finally
        {
            await cleanup.CancelAsync();
        }
    }

    [Fact]
    public void DownloadsNeedRoomOnTheirDrive()
    {
        Directory.CreateDirectory(_home);
        Assert.Throws<IOException>(() => ReleasePackage.RequireFreeSpace(_home, long.MaxValue / 2));
        ReleasePackage.RequireFreeSpace(_home, 1);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request));
    }

    /// <summary>Serves the first <c>count</c> bytes, then waits forever like a connection that went quiet.</summary>
    private sealed class StallingStream(byte[] data, int count) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_position >= count)
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            int read = Math.Min(buffer.Length, count - _position);
            data.AsMemory(_position, read).CopyTo(buffer);
            _position += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int length) =>
            ReadAsync(buffer.AsMemory(offset, length)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    }

    /// <summary>Serves the first <c>count</c> bytes, then fails like a dropped connection.</summary>
    private sealed class DroppingStream(byte[] data, int count) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int length)
        {
            if (_position >= count)
            {
                throw new IOException("The connection was reset.");
            }

            int read = Math.Min(length, count - _position);
            Array.Copy(data, _position, buffer, offset, read);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    }
}
