using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalPhotoDeleteRequest(string? ConcurrencyToken) : IStrictModuleRequest;

public static class ProfessionalPhotoEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/professionals").RequireAuthorization("Operations");
        group.MapGet("/{id:guid}/photo", Get);
        group.MapPut("/{id:guid}/photo", Put).AddEndpointFilter<AntiforgeryFilter>();
        group.MapDelete("/{id:guid}/photo", Delete).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    internal static async Task<IResult> Get(
        Guid id,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        return await ProfessionalPhotoStreaming.StreamAsync(
            professional, db, storage, loggerFactory, context, "private, no-store",
            notFoundWhenMetadataUnusable: false, cancellationToken);
    }

    private static async Task<IResult> Put(
        Guid id,
        HttpRequest request,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        IProfessionalPhotoValidator validator,
        IImageNormalizer imageNormalizer,
        IOptions<PrivateFileStorageOptions> storageOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        var outcome = await ProfessionalPhotoMutation.PutAsync(professional, request, context, db, storage, validator,
            imageNormalizer, storageOptions, timeProvider, loggerFactory, cancellationToken);
        return outcome.Succeeded ? Results.Ok(professional.ToResponse()) : outcome.ErrorResult!;
    }

    private static async Task<IResult> Delete(
        Guid id,
        [FromBody] ProfessionalPhotoDeleteRequest request,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        var outcome = await ProfessionalPhotoMutation.DeleteAsync(professional, request.ConcurrencyToken, context, db,
            storage, timeProvider, loggerFactory, cancellationToken);
        return outcome.Succeeded ? Results.Ok(professional.ToResponse()) : outcome.ErrorResult!;
    }
}
