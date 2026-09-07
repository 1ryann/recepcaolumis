using System.Text.RegularExpressions;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Professionals;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record CreateProfessionalRequest(string? Name, string? Profession, string? WhatsApp, string? Description)
    : IStrictModuleRequest;

public sealed record UpdateProfessionalRequest(
    string? Name,
    string? Profession,
    string? WhatsApp,
    string? Description,
    string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record ConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record ProfessionalResponse(
    Guid Id,
    string Name,
    string Profession,
    string WhatsApp,
    bool IsActive,
    bool HasPhoto,
    string? PhotoUrl,
    bool HasLinkedUser,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ConcurrencyToken,
    string? Description);

internal sealed record ValidProfessionalInput(string Name, string Profession, string WhatsApp, string? Description);

internal static partial class ProfessionalInput
{
    public static bool TryValidate(string? name, string? profession, string? whatsApp, string? description,
        out ValidProfessionalInput? input)
    {
        var displayName = Collapse(name);
        var displayProfession = Collapse(profession);
        if (displayName.Length is < 1 or > 200 || displayProfession.Length is < 1 or > 150 ||
            !WhatsAppNormalizer.TryNormalize(whatsApp, out var canonicalWhatsApp) ||
            !TryDescription(description, out var normalizedDescription))
        {
            input = null;
            return false;
        }

        input = new ValidProfessionalInput(displayName, displayProfession, canonicalWhatsApp, normalizedDescription);
        return true;
    }

    private static bool TryDescription(string? value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= 500 && !normalized.Contains('<') && !normalized.Contains('>');
    }

    private static string Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Whitespace().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
