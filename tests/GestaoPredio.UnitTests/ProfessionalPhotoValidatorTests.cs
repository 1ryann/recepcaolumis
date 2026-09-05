using System.Buffers.Binary;
using GestaoPredio.Infrastructure.Files;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalPhotoValidatorTests
{
    private readonly ProfessionalPhotoValidator _validator = new();

    [Theory]
    [InlineData("foto.jpg", "image/jpeg", "jpeg")]
    [InlineData("foto.jpeg", "IMAGE/JPEG", "jpeg")]
    [InlineData("foto.png", "image/png", "png")]
    [InlineData("foto.webp", "image/webp", "webp")]
    [InlineData("dra.ana.png", "image/png", "png")]
    [InlineData("perfil.2026.jpg", "image/jpeg", "jpeg")]
    [InlineData("foto.exe.png", "image/png", "png")]
    [InlineData("FOTO.JPG", "image/jpeg", "jpeg")]
    public async Task Extension_mime_and_binary_must_agree_for_valid_images(string filename, string mime, string format)
    {
        var bytes = Fixture(format);
        var result = await ValidateAsync(bytes, filename, mime);
        Assert.NotNull(result);
        Assert.Equal(mime.ToLowerInvariant(), result.MimeType);
        Assert.Equal(bytes.Length, result.Length);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Theory]
    [InlineData("foto.exe", "image/png", "png")]
    [InlineData("foto.png", "image/jpeg", "png")]
    [InlineData("foto.png", "image/png", "jpeg")]
    [InlineData("foto", "image/png", "png")]
    [InlineData("", "image/png", "png")]
    [InlineData("../foto.png", "image/png", "png")]
    [InlineData("foto.png", "application/octet-stream", "png")]
    public async Task Any_three_way_disagreement_or_untrusted_name_is_one_invalid_result(
        string filename, string mime, string format)
    {
        Assert.Null(await ValidateAsync(Fixture(format), filename, mime));
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Truncated_or_trailing_data_is_rejected(string format)
    {
        var valid = Fixture(format);
        Assert.Null(await ValidateAsync(valid[..^1], $"photo.{Extension(format)}", $"image/{MimeSubtype(format)}"));
        Assert.Null(await ValidateAsync([.. valid, 0], $"photo.{Extension(format)}", $"image/{MimeSubtype(format)}"));
    }

    [Fact]
    public async Task Png_crc_and_chunk_structure_are_validated()
    {
        var invalid = Fixture("png");
        invalid[20] ^= 1;
        Assert.Null(await ValidateAsync(invalid, "photo.png", "image/png"));
    }

    [Fact]
    public async Task Webp_riff_size_and_chunk_bounds_are_validated()
    {
        var invalid = Fixture("webp");
        invalid[4] = 0;
        Assert.Null(await ValidateAsync(invalid, "photo.webp", "image/webp"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(4097, 1)]
    [InlineData(1, 4097)]
    public async Task Invalid_dimensions_are_rejected(int width, int height)
    {
        Assert.Null(await ValidateAsync(Png(width, height), "photo.png", "image/png"));
    }

    [Fact]
    public async Task Seeded_random_inputs_never_escape_as_parser_exceptions()
    {
        var random = new Random(20260905);
        for (var index = 0; index < 200; index++)
        {
            var bytes = new byte[random.Next(0, 256)];
            random.NextBytes(bytes);
            Assert.Null(await ValidateAsync(bytes, "photo.png", "image/png"));
        }
    }

    private async Task<GestaoPredio.Application.Files.ValidatedImage?> ValidateAsync(byte[] bytes, string filename, string mime) =>
        await _validator.ValidateAsync(new MemoryStream(bytes), filename, mime, CancellationToken.None);

    private static byte[] Fixture(string format) => format switch
    {
        "jpeg" => Jpeg(),
        "png" => Png(1, 1),
        "webp" => WebP(),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string Extension(string format) => format == "jpeg" ? "jpg" : format;
    private static string MimeSubtype(string format) => format == "jpeg" ? "jpeg" : format;

    private static byte[] Jpeg() =>
    [
        0xff, 0xd8,
        0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
        0x11, 0xff, 0x00, 0x22, 0xff, 0xd9
    ];

    private static byte[] Png(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), unchecked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), unchecked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(stream, "IHDR", ihdr);
        WriteChunk(stream, "IDAT", [0x78, 0x01, 0x01, 0x05, 0x00, 0xfa, 0xff, 0x00, 0, 0, 0, 0, 0x00, 0x05, 0x00, 0x01]);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static byte[] WebP()
    {
        byte[] payload = [0x2f, 0x00, 0x00, 0x00, 0x00, 0x00];
        using var stream = new MemoryStream();
        stream.Write("RIFF"u8);
        var riffSize = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(riffSize, (uint)(4 + 8 + payload.Length));
        stream.Write(riffSize);
        stream.Write("WEBP"u8);
        stream.Write("VP8L"u8);
        BinaryPrimitives.WriteUInt32LittleEndian(riffSize, (uint)payload.Length);
        stream.Write(riffSize);
        stream.Write(payload);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
        stream.Write(value);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(value, Crc32(crcInput));
        stream.Write(value);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
