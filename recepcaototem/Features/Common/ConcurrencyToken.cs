using System.Buffers.Binary;

namespace recepcaototem.Features.Common;

public static class ConcurrencyToken
{
    public const int VersionLength = sizeof(uint);

    public static string Encode(uint version)
    {
        Span<byte> bytes = stackalloc byte[VersionLength];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string? token, out uint version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var decoded = Convert.FromBase64String(token);
            if (decoded.Length != VersionLength) return false;
            version = BinaryPrimitives.ReadUInt32BigEndian(decoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
