using GestaoPredio.Infrastructure.Files;

namespace GestaoPredio.UnitTests;

public sealed class FileSystemPrivateFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Lumis-FileStorageTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stage_commit_open_and_delete_use_backend_random_keys_without_overwrite()
    {
        var storage = CreateStorage();
        await using var source = new MemoryStream([1, 2, 3, 4]);

        var staged = await storage.StageAsync(source, 4, CancellationToken.None);

        Assert.Matches("^[a-f0-9]{32}\\.tmp$", staged.TemporaryKey);
        Assert.True(File.Exists(Path.Combine(_root, ".staging", staged.TemporaryKey)));
        var key = await storage.CommitAsync(staged, CancellationToken.None);
        Assert.Matches("^[a-f0-9]{32}$", key);
        Assert.False(File.Exists(Path.Combine(_root, ".staging", staged.TemporaryKey)));
        await using (var opened = await storage.OpenReadAsync(key, CancellationToken.None))
        {
            Assert.NotNull(opened);
            using var copy = new MemoryStream();
            await opened.CopyToAsync(copy);
            Assert.Equal([1, 2, 3, 4], copy.ToArray());
        }
        Assert.True(await storage.DeleteAsync(key, CancellationToken.None));
        Assert.False(await storage.DeleteAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Bounded_stage_rejects_oversize_and_cleans_temporary_file()
    {
        var storage = CreateStorage();
        await using var source = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.StageAsync(source, 2, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, ".staging")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_or_io_failure_cleans_temporary_file(bool cancellation)
    {
        var storage = CreateStorage();
        await using var source = new ThrowingStream(cancellation);
        await Assert.ThrowsAnyAsync<Exception>(() => storage.StageAsync(source, 100, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, ".staging")));
    }

    [Fact]
    public async Task Discard_supports_database_compensation_and_path_traversal_is_rejected()
    {
        var storage = CreateStorage();
        var staged = await storage.StageAsync(new MemoryStream([1]), 1, CancellationToken.None);
        await storage.DiscardAsync(staged, CancellationToken.None);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, ".staging")));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync("../secret", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync("..\\secret", CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_before_final_move_discards_the_staged_file()
    {
        var storage = CreateStorage();
        var staged = await storage.StageAsync(new MemoryStream([1]), 1, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => storage.CommitAsync(staged, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, ".staging")));
    }

    private FileSystemPrivateFileStorage CreateStorage()
    {
        Directory.CreateDirectory(_root);
        return new FileSystemPrivateFileStorage(new PrivateFileStorageOptions
        {
            PrivateFilesPath = _root,
            ProfessionalPhotoMaxBytes = 5 * 1024 * 1024
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class ThrowingStream(bool cancellation) : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_reads++ == 0) { buffer.Span[0] = 1; return 1; }
            if (cancellation) throw new OperationCanceledException();
            throw new IOException("simulated");
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
