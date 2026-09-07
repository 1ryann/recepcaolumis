using System.Security.Claims;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace recepcaototem.Features.Professionals;

public static class ProfessionalProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/me").RequireAuthorization("Professional");
        group.MapGet("", async (HttpContext context, ApplicationDbContext db, ILogger<ProfessionalProfileEndpointsLog> logger, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await db.Professionals.AsNoTracking()
                .Where(p => p.ApplicationUserId == userId && p.IsActive)
                .Select(p => new { p.Name, p.Profession, p.Description, HasPhoto = p.PhotoFileId != null,
                    PhotoUrl = p.PhotoFileId != null ? "/api/professional/me/photo" : null })
                .SingleOrDefaultAsync(cancellationToken);
            context.Response.Headers.CacheControl = "private, no-store";
            if (profile is null)
            {
                logger.LogWarning("Professional role has no active professional link. UserId: {UserId}", userId);
                return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });
            }
            return Results.Ok(profile);
        });
        group.MapGet("/photo", async (HttpContext context, ApplicationDbContext db, IPrivateFileStorage storage,
            ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var id = await db.Professionals.AsNoTracking()
                .Where(p => p.ApplicationUserId == userId && p.IsActive).Select(p => (Guid?)p.Id)
                .SingleOrDefaultAsync(cancellationToken);
            return id is null ? Results.NotFound() : await ProfessionalPhotoEndpoints.Get(
                id.Value, context, db, storage, loggerFactory, cancellationToken);
        });
        return endpoints;
    }
}

public sealed class ProfessionalProfileEndpointsLog;
