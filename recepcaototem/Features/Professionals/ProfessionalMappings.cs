using GestaoPredio.Domain.Professionals;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

internal static class ProfessionalMappings
{
    public static ProfessionalResponse ToResponse(this Professional professional) => new(
        professional.Id,
        professional.Name,
        professional.Profession,
        professional.WhatsApp,
        professional.IsActive,
        professional.PhotoFileId.HasValue,
        professional.PhotoFileId.HasValue ? $"/api/admin/professionals/{professional.Id}/photo" : null,
        professional.ApplicationUserId is not null,
        professional.CreatedAt,
        professional.UpdatedAt,
        ConcurrencyToken.Encode(professional.Version));
}
