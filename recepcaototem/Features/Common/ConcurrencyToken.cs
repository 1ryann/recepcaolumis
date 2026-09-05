namespace recepcaototem.Features.Common;

public static class ConcurrencyToken
{
    public const int RowVersionLength = 8;

    public static string Encode(byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        if (rowVersion.Length != RowVersionLength)
            throw new ArgumentException("A rowversion deve possuir oito bytes.", nameof(rowVersion));
        return Convert.ToBase64String(rowVersion);
    }

    public static bool TryDecode(string? token, out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var decoded = Convert.FromBase64String(token);
            if (decoded.Length != RowVersionLength) return false;
            rowVersion = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
