using System.Buffers;
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
                var invalidStream = stream;
                stream = null;
                if (invalidStream is not null) await invalidStream.DisposeAsync();
                return Unavailable();
            }
        }
        catch (OperationCanceledException)
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync(); }
                catch { }
            }
            throw;
        }
        catch (Exception)
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync(); }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return Unavailable();
        }

        return new ProtectedStreamResult(stream, metadata.MimeType, cacheControl,
            publicFailureIsNotFound, loggerFactory.CreateLogger("RoomPhoto"), photo);

        IResult Unavailable()
        {
            LogUnavailable(loggerFactory.CreateLogger("RoomPhoto"), photo, context);
            return UnavailableResult(publicFailureIsNotFound);
        }
    }

    private static void LogUnavailable(ILogger logger, RoomPhoto photo, HttpContext context) =>
        logger.LogError(
            "Room photo unavailable. RoomId={RoomId} PhotoId={PhotoId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
            photo.RoomId, photo.Id, photo.PrivateFileId, context.TraceIdentifier);

    private static IResult UnavailableResult(bool publicFailureIsNotFound) =>
        publicFailureIsNotFound ? Results.NotFound() : Results.Json(
            new ApiError("PHOTO_UNAVAILABLE", "A foto da sala está temporariamente indisponível."),
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed class ProtectedStreamResult(Stream stream, string mimeType, string cacheControl,
        bool publicFailureIsNotFound, ILogger logger, RoomPhoto photo) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81_920);
            Exception? failure = null;
            OperationCanceledException? cancellation = null;
            var responseStarted = false;

            try
            {
                // Read before sending headers so an immediate storage failure can still receive
                // the caller's safe 503/404 policy.
                var count = await stream.ReadAsync(buffer.AsMemory(0, 81_920), context.RequestAborted);
                if (count == 0)
                    throw new IOException("The room photo stream ended before any content was sent.");

                context.Response.Headers.ContentDisposition = "inline";
                context.Response.Headers.CacheControl = cacheControl;
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.ContentType = mimeType;
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                responseStarted = true;

                while ((count = await stream.ReadAsync(buffer.AsMemory(0, 81_920), context.RequestAborted)) != 0)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                    responseStarted = true;
                }
            }
            catch (OperationCanceledException exception)
            {
                cancellation = exception;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                try
                {
                    await stream.DisposeAsync();
                }
                catch (OperationCanceledException exception)
                {
                    cancellation ??= exception;
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }

            if (cancellation is not null) throw cancellation;
            if (failure is null) return;

            LogUnavailable(logger, photo, context);
            if (responseStarted || context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            context.Response.Clear();
            await UnavailableResult(publicFailureIsNotFound).ExecuteAsync(context);
        }
    }
}
