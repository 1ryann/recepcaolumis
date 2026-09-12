using GestaoPredio.Application.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GestaoPredio.Infrastructure.Files;

public sealed class ImageSharpImageNormalizer : IImageNormalizer
{
    private const int TargetSize = 512;

    public async Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(TargetSize, TargetSize),
            Mode = ResizeMode.Crop, // center-crop to exactly fill 512x512 regardless of source aspect ratio
        }));
        var output = new MemoryStream();
        await image.SaveAsync(output, new WebpEncoder(), cancellationToken);
        output.Position = 0;
        return new NormalizedImage(output, output.Length);
    }
}
