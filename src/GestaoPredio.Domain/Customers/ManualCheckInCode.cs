using System.Globalization;
using System.Security.Cryptography;

namespace GestaoPredio.Domain.Customers;

/// <summary>
/// The 6-digit manual check-in code. A string (leading zeros matter), never an int.
/// Pure value object: generate + validate only. The plaintext lives only in the issue
/// response; the persisted form is a keyed hash produced by IManualCheckInCodeHasher.
/// </summary>
public readonly struct ManualCheckInCode
{
    public string Value { get; }
    private ManualCheckInCode(string value) => Value = value;

    public static ManualCheckInCode Generate() =>
        new(RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture));

    public static bool TryParse(string? raw, out ManualCheckInCode code)
    {
        code = default;
        var t = raw?.Trim() ?? string.Empty;
        if (t.Length != 6) return false;
        foreach (var c in t) if (c is < '0' or > '9') return false;
        code = new ManualCheckInCode(t);
        return true;
    }

    public override string ToString() => Value;
}
