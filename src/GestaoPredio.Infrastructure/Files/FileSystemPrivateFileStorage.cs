using System.Text.RegularExpressions;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;

namespace GestaoPredio.Infrastructure.Files;

public sealed partial class FileSystemPrivateFileStorage : IPrivateFileStorage
{
    private const int MaximumKeyAttempts = 5;
    private readonly string _stagingRoot;
    private readonly string _filesRoot;

    public FileSystemPrivateFileStorage(PrivateFileStorageOptions options)
    {
        var root = Path.GetFullPath(options.PrivateFilesPath);
        _stagingRoot = Path.Combine(root, ".staging");
        _filesRoot = Path.Combine(root, "files");
        Directory.CreateDirectory(_stagingRoot);
        Directory.CreateDirectory(_filesRoot);
    }

    public async Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A origem deve permitir leitura.", nameof(source));
        if (maximumBytes is < 1 or > PrivateFileStorageOptions.MaximumProfessionalPhotoBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        var temporaryKey = $"{Guid.NewGuid():N}.tmp";
        var path = ResolveTemporary(temporaryKey);
        long total = 0;
        try
        {
            await using var destination = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximumBytes) throw new InvalidDataException("O arquivo excede o limite permitido.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await destination.FlushAsync(cancellationToken);
            return new StagedPrivateFile(temporaryKey, total);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        var source = ResolveTemporary(staged.TemporaryKey);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(source)) throw new FileNotFoundException("O arquivo temporário não está disponível.");
            for (var attempt = 0; attempt < MaximumKeyAttempts; attempt++)
            {
                var key = Guid.NewGuid().ToString("N");
                var destination = ResolveStorage(key);
                try
                {
                    File.Move(source, destination, overwrite: false);
                    return Task.FromResult(key);
                }
                catch (IOException) when (File.Exists(destination) && attempt + 1 < MaximumKeyAttempts)
                {
                }
            }
            throw new IOException("Não foi possível gerar uma chave privada exclusiva.");
        }
        catch
        {
            TryDelete(source);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStorage(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStorage(storageKey);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task DiscardAsync(StagedPrivateFile staged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(ResolveTemporary(staged.TemporaryKey));
        return Task.CompletedTask;
    }

    private string ResolveTemporary(string key)
    {
        if (!TemporaryKeyPattern().IsMatch(key)) throw new ArgumentException("Chave temporária inválida.", nameof(key));
        return Path.Combine(_stagingRoot, key);
    }

    private string ResolveStorage(string key)
    {
        if (!StorageKeyPattern().IsMatch(key)) throw new ArgumentException("Chave de armazenamento inválida.", nameof(key));
        return Path.Combine(_filesRoot, key);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [GeneratedRegex("^[a-f0-9]{32}\\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryKeyPattern();

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageKeyPattern();
}
