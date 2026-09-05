using System.Text.RegularExpressions;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Professionals;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record CreateProfessionalRequest(string? Name, string? Profession, string? WhatsApp)
    : IStrictModuleRequest;

public sealed record UpdateProfessionalRequest(
    string? Name,
    string? Profession,
    string? WhatsApp,
    string? ConcurrencyToken) : IStrictModuleRequest;

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
    string ConcurrencyToken);

internal sealed record ValidProfessionalInput(string Name, string Profession, string WhatsApp);

internal static partial class ProfessionalInput
{
    public static bool TryValidate(string? name, string? profession, string? whatsApp,
        out ValidProfessionalInput? input)
    {
        var displayName = Collapse(name);
        var displayProfession = Collapse(profession);
        if (displayName.Length is < 1 or > 200 || displayProfession.Length is < 1 or > 150 ||
            !WhatsAppNormalizer.TryNormalize(whatsApp, out var canonicalWhatsApp))
        {
            input = null;
            return false;
        }

        input = new ValidProfessionalInput(displayName, displayProfession, canonicalWhatsApp);
        return true;
    }

    private static string Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Whitespace().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
