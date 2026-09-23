using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.AccessControl;

/// <summary>
/// A physical access controller allowed to talk to LUMIS. Proven for the Intelbras Bio-T line
/// (SS 3532 MF, firmware V3.002.00IB000.0.R.20250625): the device is configured with a server address,
/// port and <em>path</em>, and reports to it over HTTP(S).
///
/// The protocol documents no authentication header, so the configured path carries the secret: the device
/// posts to <c>/…/{secret}</c> and only the SHA-256 of that secret is stored here, never the secret itself.
/// Revisit once the Bio-T integration manual confirms whether a stronger mechanism exists.
/// </summary>
public sealed class AccessDevice
{
    private AccessDevice() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>The serial printed in the device menu (NS). Operational identifier, not a secret.</summary>
    public string SerialNumber { get; private set; } = string.Empty;

    public byte[] SecretHash { get; private set; } = [];
    public bool IsActive { get; private set; }

    /// <summary>Last time the device reached us — an event or a keep-alive. Null until it first calls.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }

    public static AccessDevice Register(string name, string serialNumber, byte[] secretHash, DateTimeOffset occurredAt)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 100)
            throw new ArgumentException("O nome do dispositivo deve ser informado.", nameof(name));
        var normalizedSerial = serialNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSerial) || normalizedSerial.Length > 64)
            throw new ArgumentException("O número de série deve ser informado.", nameof(serialNumber));
        if (secretHash is null || secretHash.Length != 32)
            throw new ArgumentException("O hash do segredo deve ter 32 bytes.", nameof(secretHash));

        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new AccessDevice
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            SerialNumber = normalizedSerial,
            SecretHash = secretHash.ToArray(),
            IsActive = true,
            CreatedAt = timestamp
        };
    }

    public void MarkSeen(DateTimeOffset at) => LastSeenAt = TimestampNormalizer.ToUtcMicroseconds(at);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
