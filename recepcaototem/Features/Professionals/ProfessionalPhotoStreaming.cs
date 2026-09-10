using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

internal static class ProfessionalPhotoStreaming
{
    // Streams the professional's photo bytes. Caller has already decided the professional
    // is allowed to be seen. 404 when there is no photo pointer. A dangling/wrong-purpose
    // metadata pointer yields 404 when notFoundWhenMetadataUnusable is true (Totem route,
    // spec §7.4) and 503 when false (admin route, historical behaviour). A genuine storage
    // I/O failure always yields 503 for both callers.
    public static async Task<IResult> StreamAsync(
        Professional professional, ApplicationDbContext db, IPrivateFileStorage storage,
        ILoggerFactory loggerFactory, HttpContext context, string cacheControl,
        bool notFoundWhenMetadataUnusable, CancellationToken ct)
    {
        if (professional.PhotoFileId is null) return Results.NotFound();

        var metadata = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == professional.PhotoFileId, ct);
        if (metadata is null || !string.Equals(metadata.Purpose, PrivateFilePurposes.ProfessionalPhoto, StringComparison.Ordinal))
            return notFoundWhenMetadataUnusable
                ? Results.NotFound()
                : PhotoUnavailable(loggerFactory, context, professional.Id, professional.PhotoFileId.Value);

        Stream? stream;
        try
        {
            stream = await storage.OpenReadAsync(metadata.StorageKey, ct);
            if (stream is null || !stream.CanSeek || stream.Length != metadata.Length)
            {
                if (stream is not null) await stream.DisposeAsync();
                return PhotoUnavailable(loggerFactory, context, professional.Id, metadata.Id);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PhotoUnavailable(loggerFactory, context, professional.Id, metadata.Id);
        }

        context.Response.Headers.ContentDisposition = "inline";
        context.Response.Headers.CacheControl = cacheControl;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(stream, metadata.MimeType, enableRangeProcessing: false);
    }

    private static IResult PhotoUnavailable(ILoggerFactory lf, HttpContext ctx, Guid professionalId, Guid fileId)
    {
        lf.CreateLogger("ProfessionalPhoto").LogError(
            "Professional photo unavailable. ProfessionalId={ProfessionalId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
            professionalId, fileId, ctx.TraceIdentifier);
        return Results.Json(new ApiError("PHOTO_UNAVAILABLE", "A foto do profissional está temporariamente indisponível."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
