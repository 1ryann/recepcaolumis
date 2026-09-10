using GestaoPredio.Domain.Customers;

namespace GestaoPredio.Application.Customers;

/// <summary>
/// Keyed hash of the 6-digit manual check-in code. HMAC-SHA-256 with a dedicated
/// server secret so a database leak cannot brute-force the 1,000,000-combination
/// space offline. Deterministic, 32 bytes, indexable, never the plaintext.
/// </summary>
public interface IManualCheckInCodeHasher
{
    byte[] Hash(ManualCheckInCode code);
}
