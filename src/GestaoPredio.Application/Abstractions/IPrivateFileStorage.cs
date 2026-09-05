using GestaoPredio.Application.Files;

namespace GestaoPredio.Application.Abstractions;

public interface IPrivateFileStorage
{
    Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken cancellationToken);
    Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken);
    Task DiscardAsync(StagedPrivateFile staged, CancellationToken cancellationToken);
}
