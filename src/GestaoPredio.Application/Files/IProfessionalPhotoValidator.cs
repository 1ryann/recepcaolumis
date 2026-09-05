namespace GestaoPredio.Application.Files;

public interface IProfessionalPhotoValidator
{
    Task<ValidatedImage?> ValidateAsync(Stream content, string? fileName, string? declaredContentType,
        CancellationToken cancellationToken);
}
