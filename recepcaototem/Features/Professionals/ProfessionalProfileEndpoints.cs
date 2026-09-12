using System.Security.Claims;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public static class ProfessionalProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/me").RequireAuthorization("Professional");
        group.MapGet("", async (HttpContext context, ApplicationDbContext db, ILogger<ProfessionalProfileEndpointsLog> logger, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var professional = await db.Professionals.AsNoTracking()
                .Where(p => p.ApplicationUserId == userId && p.IsActive)
                .Select(p => new ProfessionalProfileResponse(
                    p.Name, p.Profession, p.Description, p.WhatsApp,
                    p.PhotoFileId != null, p.PhotoFileId != null ? "/api/professional/me/photo" : null,
                    ConcurrencyToken.Encode(p.Version)))
                .SingleOrDefaultAsync(cancellationToken);
            context.Response.Headers.CacheControl = "private, no-store";
            if (professional is null)
            {
                logger.LogWarning("Professional role has no active professional link. UserId: {UserId}", userId);
                return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });
            }
            return Results.Ok(professional);
        });
        group.MapPut("", async (
            ProfessionalProfileUpdateRequest request, HttpContext context, ApplicationDbContext db,
            TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
                return ProfessionalEndpoints.InvalidToken();

            var professional = await db.Professionals
                .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
            if (professional is null)
                return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });
            if (professional.Version != expectedVersion)
                return ProfessionalEndpoints.Modified();

            if (!WhatsAppNormalizer.TryNormalize(request.WhatsApp, out var canonicalWhatsApp) ||
                !ProfessionalInput.TryDescription(request.Description, out var description))
                return Results.BadRequest(new ApiError("INVALID_PROFESSIONAL", "Os dados do profissional são inválidos."));

            var now = timeProvider.GetUtcNow();
            db.Entry(professional).Property(x => x.Version).OriginalValue = expectedVersion;
            professional.Update(professional.Name, professional.Profession, canonicalWhatsApp, now, description);
            db.AuditEntries.Add(ProfessionalEndpoints.CreateAudit(context, professional.Id, "PROFESSIONAL_PROFILE_UPDATED", now));

            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return ProfessionalEndpoints.Modified(); }

            return Results.Ok(new ProfessionalProfileResponse(
                professional.Name, professional.Profession, professional.Description, professional.WhatsApp,
                professional.PhotoFileId != null, professional.PhotoFileId != null ? "/api/professional/me/photo" : null,
                ConcurrencyToken.Encode(professional.Version)));
        }).AddEndpointFilter<AntiforgeryFilter>();
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
        group.MapPost("/photo", async (
            HttpRequest request, HttpContext context, ApplicationDbContext db, IPrivateFileStorage storage,
            IProfessionalPhotoValidator validator, IImageNormalizer imageNormalizer,
            IOptions<PrivateFileStorageOptions> storageOptions, ProfessionalPhotoUploadRateLimiter rateLimiter,
            TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            using var lease = await rateLimiter.AcquireAsync(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", userId, cancellationToken);
            if (!lease.IsAcquired) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            var professional = await db.Professionals
                .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
            if (professional is null)
                return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });

            var outcome = await ProfessionalPhotoMutation.PutAsync(professional, request, context, db, storage,
                validator, imageNormalizer, storageOptions, timeProvider, loggerFactory, cancellationToken);
            if (!outcome.Succeeded) return outcome.ErrorResult!;
            return Results.Ok(new
            {
                hasPhoto = true,
                photoUrl = "/api/professional/me/photo",
                concurrencyToken = ConcurrencyToken.Encode(professional.Version)
            });
        }).AddEndpointFilter<AntiforgeryFilter>();
        group.MapDelete("/photo", async (
            [FromBody] ProfessionalPhotoDeleteRequest request, HttpContext context, ApplicationDbContext db,
            IPrivateFileStorage storage, TimeProvider timeProvider, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var professional = await db.Professionals
                .SingleOrDefaultAsync(p => p.ApplicationUserId == userId && p.IsActive, cancellationToken);
            if (professional is null)
                return Results.NotFound(new { code = "PROFESSIONAL_PROFILE_NOT_LINKED", message = "O perfil profissional não está vinculado corretamente." });

            var outcome = await ProfessionalPhotoMutation.DeleteAsync(professional, request.ConcurrencyToken,
                context, db, storage, timeProvider, loggerFactory, cancellationToken);
            if (!outcome.Succeeded) return outcome.ErrorResult!;
            return Results.Ok(new
            {
                hasPhoto = false,
                photoUrl = (string?)null,
                concurrencyToken = ConcurrencyToken.Encode(professional.Version)
            });
        }).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }
}

public sealed class ProfessionalProfileEndpointsLog;
