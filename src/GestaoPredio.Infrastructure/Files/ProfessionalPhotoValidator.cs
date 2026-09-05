using GestaoPredio.Application.Files;
using GestaoPredio.Infrastructure.Files.ImageParsers;

namespace GestaoPredio.Infrastructure.Files;

public sealed class ProfessionalPhotoValidator : IProfessionalPhotoValidator
{
    public async Task<ValidatedImage?> ValidateAsync(Stream content, string? fileName, string? declaredContentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(declaredContentType)) return null;
        var logicalName = Path.GetFileName(fileName);
        if (logicalName.Length == 0 || !string.Equals(logicalName, fileName, StringComparison.Ordinal)) return null;
        var extension = Path.GetExtension(logicalName);
        var expectedMime = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (expectedMime is null || !string.Equals(expectedMime, declaredContentType.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(bytes, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > PrivateFileStorageOptions.MaximumProfessionalPhotoBytes) return null;
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
        }
        var data = buffer.ToArray();
        ImageDimensions dimensions = default;
        var actualMime = data.AsSpan().StartsWith(new byte[] { 0xff, 0xd8 }) && JpegParser.TryParse(data, out dimensions)
            ? "image/jpeg"
            : data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && PngParser.TryParse(data, out dimensions)
                ? "image/png"
                : data.AsSpan().StartsWith("RIFF"u8) && WebPParser.TryParse(data, out dimensions)
                    ? "image/webp"
                    : null;
        return actualMime is not null && string.Equals(actualMime, expectedMime, StringComparison.Ordinal)
            ? new ValidatedImage(actualMime, total, dimensions.Width, dimensions.Height)
            : null;
    }
}
