using System.Security.Cryptography;
using System.Text;

namespace GestaoPredio.Infrastructure.Whatsapp;

/// <summary>
/// Meta webhook authenticity checks: the GET subscription handshake compares hub.verify_token with
/// Whatsapp:VerifyToken, and every POST carries X-Hub-Signature-256 = "sha256=" + HMAC-SHA256(AppSecret, raw body).
/// Both comparisons are constant-time and fail closed when the secret is not configured.
/// </summary>
public static class WhatsAppWebhookSecurity
{
    public const string SignatureHeader = "X-Hub-Signature-256";
    private const string SignaturePrefix = "sha256=";

    public static bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, string? appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            return false;

        var hex = signatureHeader.AsSpan(SignaturePrefix.Length).Trim();
        if (hex.Length != 64) return false;
        Span<byte> provided = stackalloc byte[32];
        if (Convert.FromHexString(hex, provided, out _, out var written) != System.Buffers.OperationStatus.Done || written != 32)
            return false;

        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody, expected);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public static bool IsVerifyTokenValid(string? provided, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || provided is null) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
