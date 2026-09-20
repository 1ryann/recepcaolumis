using GestaoPredio.Application.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GestaoPredio.Infrastructure.Files;

/// <summary>
/// Room photos, unlike profile photos, are shown large — full width on the Totem and on the
/// public page — and what matters is the room, edge to edge. So this keeps the original
/// aspect ratio (never a centre crop), bounds the longest side to <see cref="MaximumSide"/>
/// and never upscales a smaller photo. WebP keeps the file small at that size.
/// </summary>
public sealed class RoomImageNormalizer : IRoomImageNormalizer
{
    public const int MaximumSide = 1600;
    private const int Quality = 82;

    public async Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        var longest = Math.Max(image.Width, image.Height);
        if (longest > MaximumSide)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaximumSide, MaximumSide),
                Mode = ResizeMode.Max, // fits inside the box, keeping the proportions
            }));
        }

        var output = new MemoryStream();
        await image.SaveAsync(output, new WebpEncoder { Quality = Quality }, cancellationToken);
        output.Position = 0;
        return new NormalizedImage(output, output.Length);
    }
}
