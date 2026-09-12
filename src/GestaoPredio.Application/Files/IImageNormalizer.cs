namespace GestaoPredio.Application.Files;

public interface IImageNormalizer
{
    Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken);
}
