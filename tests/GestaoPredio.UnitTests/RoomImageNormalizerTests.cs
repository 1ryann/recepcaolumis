using GestaoPredio.Infrastructure.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GestaoPredio.UnitTests;

// A room is photographed in landscape and shown large on the Totem and on the public page,
// so its photo keeps its shape and enough pixels to fill a screen — unlike a profile photo,
// which is a square avatar. These tests pin that difference.
public sealed class RoomImageNormalizerTests
{
    [Fact]
    public async Task A_landscape_photo_keeps_its_aspect_ratio_and_is_never_cropped()
    {
        using var source = new Image<Rgba32>(3000, 2000); // 3:2, a phone photo
        using var input = new MemoryStream();
        await source.SaveAsJpegAsync(input);
        input.Position = 0;

        var result = await new RoomImageNormalizer().NormalizeAsync(input, CancellationToken.None);

        result.Content.Position = 0;
        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(1600, output.Width);
        Assert.Equal(1067, output.Height); // 3:2 preserved, not squared off
    }

    [Fact]
    public async Task A_portrait_photo_is_bounded_by_its_longest_side()
    {
        using var source = new Image<Rgba32>(1500, 3000);
        using var input = new MemoryStream();
        await source.SaveAsPngAsync(input);
        input.Position = 0;

        var result = await new RoomImageNormalizer().NormalizeAsync(input, CancellationToken.None);

        result.Content.Position = 0;
        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(1600, output.Height);
        Assert.Equal(800, output.Width);
    }

    [Fact]
    public async Task A_photo_smaller_than_the_limit_is_not_enlarged()
    {
        using var source = new Image<Rgba32>(900, 600);
        using var input = new MemoryStream();
        await source.SaveAsPngAsync(input);
        input.Position = 0;

        var result = await new RoomImageNormalizer().NormalizeAsync(input, CancellationToken.None);

        result.Content.Position = 0;
        using var output = await Image.LoadAsync(result.Content, CancellationToken.None);
        Assert.Equal(900, output.Width);
        Assert.Equal(600, output.Height);
    }

    [Fact]
    public async Task The_result_is_webp_and_reports_its_own_length()
    {
        using var source = new Image<Rgba32>(2400, 1600);
        using var input = new MemoryStream();
        await source.SaveAsJpegAsync(input);
        input.Position = 0;

        var result = await new RoomImageNormalizer().NormalizeAsync(input, CancellationToken.None);

        Assert.IsType<WebpFormat>(await Image.DetectFormatAsync(result.Content));
        Assert.Equal(result.Content.Length, result.Length);
    }
}
