using System.Buffers.Binary;

namespace GestaoPredio.Infrastructure.Files.ImageParsers;

internal static class PngParser
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryParse(ReadOnlySpan<byte> data, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature)) return false;
        var index = 8;
        var chunks = 0;
        var sawHeader = false;
        var sawData = false;
        var dataEnded = false;
        while (index < data.Length && chunks++ < 10000)
        {
            if (index + 12 > data.Length) return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(index, 4));
            if (length > int.MaxValue) return false;
            var chunkLength = (int)length;
            var end = (long)index + 12 + chunkLength;
            if (end > data.Length) return false;
            var type = data.Slice(index + 4, 4);
            if (!IsChunkType(type)) return false;
            var content = data.Slice(index + 8, chunkLength);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(index + 8 + chunkLength, 4));
            if (Crc32(data.Slice(index + 4, 4 + chunkLength)) != storedCrc) return false;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || index != 8 || chunkLength != 13) return false;
                var width = BinaryPrimitives.ReadUInt32BigEndian(content[..4]);
                var height = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4));
                if (width > int.MaxValue || height > int.MaxValue) return false;
                dimensions = new ImageDimensions((int)width, (int)height);
                if (!dimensions.IsSafe || !ValidHeader(content)) return false;
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader || dataEnded) return false;
                sawData = true;
            }
            else
            {
                if (sawData) dataEnded = true;
                if (type.SequenceEqual("IEND"u8))
                    return sawHeader && sawData && chunkLength == 0 && end == data.Length;
                if ((type[0] & 0x20) == 0 && !type.SequenceEqual("PLTE"u8)) return false;
            }
            index = (int)end;
        }
        return false;
    }

    private static bool ValidHeader(ReadOnlySpan<byte> header)
    {
        var bitDepth = header[8];
        var colorType = header[9];
        var validDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 or 6 => bitDepth is 8 or 16,
            _ => false
        };
        return validDepth && header[10] == 0 && header[11] == 0 && header[12] <= 1;
    }

    private static bool IsChunkType(ReadOnlySpan<byte> type)
    {
        foreach (var value in type)
            if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')) return false;
        return true;
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
