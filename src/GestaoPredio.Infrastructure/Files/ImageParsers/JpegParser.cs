using System.Buffers.Binary;

namespace GestaoPredio.Infrastructure.Files.ImageParsers;

internal static class JpegParser
{
    public static bool TryParse(ReadOnlySpan<byte> data, out ImageDimensions dimensions)
    {
        dimensions = default;
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8) return false;
        var index = 2;
        var segments = 0;
        var inScan = false;
        var sawScan = false;
        var sawFrame = false;

        while (index < data.Length && segments++ < 4096)
        {
            byte marker;
            if (inScan)
            {
                while (index < data.Length && data[index] != 0xff) index++;
                if (index >= data.Length) return false;
                while (index < data.Length && data[index] == 0xff) index++;
                if (index >= data.Length) return false;
                marker = data[index++];
                if (marker == 0x00 || marker is >= 0xd0 and <= 0xd7) continue;
                inScan = false;
            }
            else
            {
                if (data[index++] != 0xff) return false;
                while (index < data.Length && data[index] == 0xff) index++;
                if (index >= data.Length) return false;
                marker = data[index++];
            }

            if (marker == 0xd9) return sawFrame && sawScan && index == data.Length && dimensions.IsSafe;
            if (marker is 0xd8 or 0x00) return false;
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (index + 2 > data.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index, 2));
            if (length < 2 || index + length > data.Length) return false;

            if (IsStartOfFrame(marker))
            {
                if (sawFrame || length < 8) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 5, 2));
                dimensions = new ImageDimensions(width, height);
                if (!dimensions.IsSafe) return false;
                sawFrame = true;
            }
            else if (marker == 0xda)
            {
                if (!sawFrame || length < 6) return false;
                sawScan = true;
                inScan = true;
            }
            index += length;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc);
}
