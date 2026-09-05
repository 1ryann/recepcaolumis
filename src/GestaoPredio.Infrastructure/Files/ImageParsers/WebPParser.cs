using System.Buffers.Binary;

namespace GestaoPredio.Infrastructure.Files.ImageParsers;

internal static class WebPParser
{
    public static bool TryParse(ReadOnlySpan<byte> data, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (data.Length < 20 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return false;
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (riffSize != data.Length - 8) return false;
        var index = 12;
        var chunks = 0;
        var imageChunks = 0;
        ImageDimensions? extended = null;
        while (index < data.Length && chunks++ < 10000)
        {
            if (index + 8 > data.Length) return false;
            var type = data.Slice(index, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(index + 4, 4));
            if (length > int.MaxValue) return false;
            var contentStart = index + 8;
            var contentEnd = (long)contentStart + length;
            var paddedEnd = contentEnd + (length & 1);
            if (paddedEnd > data.Length) return false;
            var content = data.Slice(contentStart, (int)length);

            if (type.SequenceEqual("VP8 "u8))
            {
                if (!TryVp8(content, out dimensions)) return false;
                imageChunks++;
            }
            else if (type.SequenceEqual("VP8L"u8))
            {
                if (!TryVp8L(content, out dimensions)) return false;
                imageChunks++;
            }
            else if (type.SequenceEqual("VP8X"u8))
            {
                if (extended.HasValue || content.Length != 10 || (content[0] & 0xc1) != 0 ||
                    content[1] != 0 || content[2] != 0 || content[3] != 0) return false;
                extended = new ImageDimensions(Read24(content.Slice(4, 3)) + 1, Read24(content.Slice(7, 3)) + 1);
                if (!extended.Value.IsSafe) return false;
            }
            if ((length & 1) != 0 && data[(int)contentEnd] != 0) return false;
            index = (int)paddedEnd;
        }
        if (index != data.Length || imageChunks != 1 || !dimensions.IsSafe) return false;
        return !extended.HasValue || extended.Value == dimensions;
    }

    private static bool TryVp8(ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (content.Length < 10 || (content[0] & 1) != 0 ||
            !content.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
            return false;
        dimensions = new ImageDimensions(
            BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(6, 2)) & 0x3fff,
            BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(8, 2)) & 0x3fff);
        return dimensions.IsSafe;
    }

    private static bool TryVp8L(ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (content.Length < 5 || content[0] != 0x2f) return false;
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(1, 4));
        if ((bits >> 29) != 0) return false;
        dimensions = new ImageDimensions((int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1);
        return dimensions.IsSafe;
    }

    private static int Read24(ReadOnlySpan<byte> value) => value[0] | value[1] << 8 | value[2] << 16;
}
