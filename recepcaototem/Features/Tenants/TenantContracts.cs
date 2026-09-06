using System.Text.RegularExpressions;
using GestaoPredio.Domain.Tenants;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Tenants;

public sealed record CreateTenantRequest(string? Name, string? Kind) : IStrictModuleRequest;
public sealed record UpdateTenantRequest(string? Name, string? Kind, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record TenantConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record TenantResponse(Guid Id, string Name, string Kind, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ConcurrencyToken);

internal static partial class TenantInput
{
    public static bool TryValidate(string? name, string? kind, out string cleanName, out TenantKind parsedKind)
    {
        cleanName = string.IsNullOrWhiteSpace(name) ? "" : Whitespace().Replace(name.Trim(), " ");
        parsedKind = kind switch
        {
            "INDIVIDUAL" => TenantKind.Individual,
            "LEGAL_ENTITY" => TenantKind.LegalEntity,
            _ => default
        };
        return cleanName.Length is >= 1 and <= Tenant.MaximumNameLength && parsedKind != default;
    }

    public static string ToContract(this TenantKind kind) => kind switch
    {
        TenantKind.Individual => "INDIVIDUAL",
        TenantKind.LegalEntity => "LEGAL_ENTITY",
        _ => throw new InvalidOperationException("Tipo de locatário desconhecido.")
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
