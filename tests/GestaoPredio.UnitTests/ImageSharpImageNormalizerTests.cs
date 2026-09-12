using GestaoPredio.Infrastructure.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class ImageSharpImageNormalizerTests
{
    [Fact]
    public async Task NormalizeAsync_produces_512x512_webp_from_a_non_square_png()
    {
        using var source = new Image<Rgba32>(300, 600); // tall rectangle, forces a center-crop
        using var input = new MemoryStream();
        await source.SaveAsPngAsync(input);
        input.Position = 0;

        var normalizer = new ImageSharpImageNormalizer();
        var result = await normalizer.NormalizeAsync(input, CancellationToken.None);

        var format = await Image.DetectFormatAsync(result.Content);
        Assert.IsType<WebpFormat>(format);

        result.Content.Position = 0;
        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(512, output.Width);
        Assert.Equal(512, output.Height);
    }

    [Fact]
    public async Task NormalizeAsync_produces_512x512_webp_from_a_wide_png()
    {
        using var source = new Image<Rgba32>(800, 200); // wide rectangle, forces a center-crop
        using var input = new MemoryStream();
        await source.SaveAsPngAsync(input);
        input.Position = 0;

        var normalizer = new ImageSharpImageNormalizer();
        var result = await normalizer.NormalizeAsync(input, CancellationToken.None);

        result.Content.Position = 0;
        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(512, output.Width);
        Assert.Equal(512, output.Height);
        Assert.Equal(result.Content.Length, result.Length);
    }
}
