using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Application.Customers;
using GestaoPredio.Domain.Customers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Customers;

/// <summary>
/// HMAC-SHA-256 keyed hash of the manual check-in code (spec 7A.3b / 7A.11).
/// Fail-closed in Production when the dedicated secret is absent; an explicit,
/// deterministic dev fallback is used only outside Production. The key is never logged.
/// </summary>
internal sealed class HmacManualCheckInCodeHasher : IManualCheckInCodeHasher
{
    private readonly byte[] _key;

    public HmacManualCheckInCodeHasher(IOptions<ManualCheckInCodeHashingOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.ManualCodeHmacKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (env.IsProduction())
                throw new InvalidOperationException(
                    "CheckIn:ManualCodeHmacKey ausente. O código de check-in de 6 dígitos exige um segredo HMAC dedicado em Production.");
            configured = "dev-only-manual-check-in-hmac-key-do-not-use-in-prod";
        }
        _key = Encoding.UTF8.GetBytes(configured);
    }

    public byte[] Hash(ManualCheckInCode code) =>
        HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(code.Value));
}
