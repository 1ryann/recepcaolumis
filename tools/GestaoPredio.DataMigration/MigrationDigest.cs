using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GestaoPredio.DataMigration;

public static class MigrationDigest
{
    public static string Compute(IEnumerable<object?[]> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        foreach (var value in row)
        {
            if (value is null or DBNull)
            {
                hash.AppendData(BitConverter.GetBytes(-1));
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(Canonical(MigrationValue.Normalize(value)));
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Canonical(object value) => value switch
    {
        DateTimeOffset offset => offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "1" : "0",
        Guid guid => guid.ToString("D"),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
