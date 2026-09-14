using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public static class RoomPhotoStreaming
{
    // Visibility is checked by the caller; public consumers hide both metadata and storage failures.
    public static async Task<IResult> StreamAsync(RoomPhoto photo, ApplicationDbContext db,
        IPrivateFileStorage storage, ILoggerFactory loggerFactory, HttpContext context,
        string cacheControl, bool publicFailureIsNotFound, CancellationToken cancellationToken)
    {
        var metadata = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == photo.PrivateFileId, cancellationToken);
        if (metadata is null || !string.Equals(metadata.Purpose, PrivateFilePurposes.RoomPhoto, StringComparison.Ordinal))
            return Unavailable();

        Stream? stream = null;
        try
        {
            stream = await storage.OpenReadAsync(metadata.StorageKey, cancellationToken);
            if (stream is null || !stream.CanSeek || stream.Length != metadata.Length)
            {
                if (stream is not null) await stream.DisposeAsync();
                return Unavailable();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync(); }
                catch (Exception disposalException) when (disposalException is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            }
            return Unavailable();
        }

        context.Response.Headers.ContentDisposition = "inline";
        context.Response.Headers.CacheControl = cacheControl;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(stream, metadata.MimeType, enableRangeProcessing: false);

        IResult Unavailable()
        {
            loggerFactory.CreateLogger("RoomPhoto").LogError(
                "Room photo unavailable. RoomId={RoomId} PhotoId={PhotoId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
                photo.RoomId, photo.Id, photo.PrivateFileId, context.TraceIdentifier);
            return publicFailureIsNotFound ? Results.NotFound() : Results.Json(
                new ApiError("PHOTO_UNAVAILABLE", "A foto da sala está temporariamente indisponível."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
