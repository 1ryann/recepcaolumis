using GestaoPredio.Domain.Tenants;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Tenants;

internal static class TenantMappings
{
    public static TenantResponse ToResponse(this Tenant tenant) => new(
        tenant.Id, tenant.Name, tenant.Kind.ToContract(), tenant.IsActive,
        tenant.CreatedAt, tenant.UpdatedAt, ConcurrencyToken.Encode(tenant.Version));
}
