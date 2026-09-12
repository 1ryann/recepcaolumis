using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace GestaoPredio.IntegrationTests;

internal static class TestImageData
{
    public static byte[] Jpeg() =>
    [
        0xff, 0xd8,
        0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
        0x11, 0xff, 0x00, 0x22, 0xff, 0xd9
    ];

    public static byte[] Png()
    {
        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), 1);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(stream, "IHDR", ihdr);
        WriteChunk(stream, "IDAT", [0x78, 0x01, 0x01, 0x05, 0x00, 0xfa, 0xff, 0x00, 0, 0, 0, 0, 0x00, 0x05, 0x00, 0x01]);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    /// <summary>
    /// Produces a real PNG with the given pixel dimensions (via ImageSharp), for tests that need
    /// a non-trivial source image (e.g. to exercise center-crop normalization end to end).
    /// </summary>
    public static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // A real, fully decodable WebP image (via ImageSharp) rather than a hand-crafted minimal
    // bitstream: once the endpoint began decoding uploaded photos (for 512x512 normalization),
    // a synthetic-but-undecoable VP8L payload that merely satisfied the header-only WebPParser
    // sniff check started failing at decode time. A real encode keeps this fixture valid at both
    // layers.
    public static byte[] WebP()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder());
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
        var crcInput = new byte[4 + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, 4);
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
