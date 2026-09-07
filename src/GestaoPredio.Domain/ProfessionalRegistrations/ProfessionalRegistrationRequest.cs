using System.Text;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.Domain.ProfessionalRegistrations;

public enum ProfessionalRegistrationStatus { Pending, Approved, Rejected }

public sealed class ProfessionalRegistrationRequest
{
    private ProfessionalRegistrationRequest() { }
    public Guid Id { get; private set; }
    public string ApplicationUserId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Profession { get; private set; } = "";
    public string WhatsApp { get; private set; } = "";
    public string? Description { get; private set; }
    public ProfessionalRegistrationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedByUserId { get; private set; }
    public uint Version { get; private set; }

    public static ProfessionalRegistrationRequest Create(string userId, string name, string profession,
        string whatsApp, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Usuário obrigatório.", nameof(userId));
        var normalizedName = Collapse(name);
        var normalizedProfession = Collapse(profession);
        if (normalizedName.Length is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(name));
        if (normalizedProfession.Length is < 1 or > 150) throw new ArgumentOutOfRangeException(nameof(profession));
        if (!WhatsAppNormalizer.TryNormalize(whatsApp, out var phone)) throw new ArgumentException("WhatsApp inválido.", nameof(whatsApp));
        var normalizedDescription = NormalizeDescription(description);
        return new ProfessionalRegistrationRequest { Id = Guid.NewGuid(), ApplicationUserId = userId,
            Name = normalizedName, Profession = normalizedProfession, WhatsApp = phone,
            Description = normalizedDescription, Status = ProfessionalRegistrationStatus.Pending,
            CreatedAt = now.ToUniversalTime() };
    }

    public void Approve(string reviewerId, DateTimeOffset now) => Review(ProfessionalRegistrationStatus.Approved, reviewerId, now);
    public void Reject(string reviewerId, DateTimeOffset now) => Review(ProfessionalRegistrationStatus.Rejected, reviewerId, now);
    private void Review(ProfessionalRegistrationStatus status, string reviewerId, DateTimeOffset now)
    {
        if (Status != ProfessionalRegistrationStatus.Pending) throw new InvalidOperationException("Solicitação já revisada.");
        if (string.IsNullOrWhiteSpace(reviewerId)) throw new ArgumentException("Revisor obrigatório.", nameof(reviewerId));
        Status = status; ReviewedByUserId = reviewerId; ReviewedAt = now.ToUniversalTime();
    }
    private static string Collapse(string value)
    {
        ArgumentNullException.ThrowIfNull(value); var result = new StringBuilder(); var space = false;
        foreach (var c in value.Trim()) { if (char.IsWhiteSpace(c)) { space = result.Length > 0; continue; } if (space) result.Append(' '); result.Append(c); space = false; }
        return result.ToString();
    }
    private static string? NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > 500 || result.Contains('<') || result.Contains('>')) throw new ArgumentOutOfRangeException(nameof(value));
        return result;
    }
}
