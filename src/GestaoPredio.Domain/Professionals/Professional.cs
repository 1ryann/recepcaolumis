using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Professionals;

public sealed class Professional
{
    private Professional()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string Profession { get; private set; } = "";
    public string NormalizedProfession { get; private set; } = "";
    public string? Description { get; private set; }
    public string WhatsApp { get; private set; } = "";
    public Guid? PhotoFileId { get; private set; }
    public string? ApplicationUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Professional Create(string name, string profession, string whatsApp, DateTimeOffset occurredAt, string? description = null)
    {
        var professional = new Professional
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt),
            UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };

        professional.SetProfile(name, profession, whatsApp, description);
        return professional;
    }

    public void Update(string name, string profession, string whatsApp, DateTimeOffset occurredAt, string? description = null)
    {
        SetProfile(name, profession, whatsApp, description);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Activate(DateTimeOffset occurredAt) => SetActive(true, occurredAt);

    public void Deactivate(DateTimeOffset occurredAt) => SetActive(false, occurredAt);

    public void SetPhoto(Guid photoFileId, DateTimeOffset occurredAt)
    {
        if (photoFileId == Guid.Empty)
        {
            throw new ArgumentException("A foto deve ter uma identidade técnica válida.", nameof(photoFileId));
        }

        PhotoFileId = photoFileId;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void RemovePhoto(DateTimeOffset occurredAt)
    {
        PhotoFileId = null;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void LinkUser(string applicationUserId, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            throw new ArgumentException("O usuário deve ser informado.", nameof(applicationUserId));
        }

        ApplicationUserId = applicationUserId;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void UnlinkUser(DateTimeOffset occurredAt)
    {
        ApplicationUserId = null;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    private void SetProfile(string name, string profession, string whatsApp, string? description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(profession);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome deve ser informado.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(profession))
        {
            throw new ArgumentException("A profissão deve ser informada.", nameof(profession));
        }

        if (!WhatsAppNormalizer.TryNormalize(whatsApp, out var canonicalWhatsApp))
        {
            throw new ArgumentException("O WhatsApp deve ser um número E.164 válido.", nameof(whatsApp));
        }

        Name = name;
        NormalizedName = TextNormalizer.Normalize(name);
        Profession = profession;
        NormalizedProfession = TextNormalizer.Normalize(profession);
        Description = NormalizeDescription(description);
        WhatsApp = canonicalWhatsApp;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var trimmed = description.Trim();
        if (trimmed.Length > 500 || trimmed.Contains('<') || trimmed.Contains('>'))
            throw new ArgumentOutOfRangeException(nameof(description), "A descrição deve ter até 500 caracteres e não conter HTML.");
        return trimmed;
    }

    private void SetActive(bool isActive, DateTimeOffset occurredAt)
    {
        IsActive = isActive;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }
}
