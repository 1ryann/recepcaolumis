using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Tenants;

public sealed class Tenant
{
    public const int MaximumNameLength = 200;
    public const int MaximumNormalizedNameLength = 400;

    private Tenant()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public TenantKind Kind { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Tenant Create(string name, TenantKind kind, DateTimeOffset occurredAt)
    {
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        tenant.SetDetails(name, kind);
        return tenant;
    }

    public void Update(string name, TenantKind kind, DateTimeOffset occurredAt)
    {
        SetDetails(name, kind);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Activate(DateTimeOffset occurredAt) => SetActive(true, occurredAt);

    public void Deactivate(DateTimeOffset occurredAt) => SetActive(false, occurredAt);

    private void SetDetails(string name, TenantKind kind)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome deve ser informado.", nameof(name));
        if (name.Length > MaximumNameLength)
            throw new ArgumentOutOfRangeException(nameof(name));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var normalizedName = TextNormalizer.Normalize(name);
        if (normalizedName.Length > MaximumNormalizedNameLength)
            throw new ArgumentOutOfRangeException(nameof(name));

        Name = name;
        NormalizedName = normalizedName;
        Kind = kind;
    }

    private void SetActive(bool isActive, DateTimeOffset occurredAt)
    {
        IsActive = isActive;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }
}
