using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Professionals;
using System.Text;

namespace GestaoPredio.Domain.Customers;

public sealed class Customer
{
    public const int MaximumNameLength = 200;
    public const int MaximumPhoneLength = 16;

    private Customer() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string NormalizedPhone { get; private set; } = "";
    public string? ApplicationUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Customer Create(string name, string phone, DateTimeOffset occurredAt)
    {
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        customer.SetDetails(name, phone);
        return customer;
    }

    public void UpdateName(string name, DateTimeOffset occurredAt)
    {
        Name = NormalizeName(name);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void LinkUser(string applicationUserId, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId)) throw new ArgumentException("O usuário deve ser informado.", nameof(applicationUserId));
        ApplicationUserId = applicationUserId;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Deactivate(DateTimeOffset occurredAt) => SetActive(false, occurredAt);
    public void Activate(DateTimeOffset occurredAt) => SetActive(true, occurredAt);

    private void SetDetails(string name, string phone)
    {
        Name = NormalizeName(name);
        if (!WhatsAppNormalizer.TryNormalize(phone, out var canonical))
            throw new ArgumentException("O telefone deve ser um número E.164 válido.", nameof(phone));
        if (canonical.Length > MaximumPhoneLength) throw new ArgumentOutOfRangeException(nameof(phone));
        Phone = canonical;
        NormalizedPhone = canonical;
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = CollapseWhitespace(name);
        if (normalized.Length is < 1 or > MaximumNameLength) throw new ArgumentOutOfRangeException(nameof(name));
        return normalized;
    }

    private static string CollapseWhitespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            builder.Append(character);
            pendingSpace = false;
        }
        return builder.ToString();
    }

    private void SetActive(bool active, DateTimeOffset occurredAt)
    {
        IsActive = active;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }
}
