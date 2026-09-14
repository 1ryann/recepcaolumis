using System.Security.Claims;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public static class RoomPhotoMutation
{
    private const long MultipartOverheadAllowance = 64 * 1024;

    public static async Task<IResult> UploadAsync(Guid roomId, HttpRequest request, HttpContext context,
        ApplicationDbContext db, IPrivateFileStorage storage, IProfessionalPhotoValidator validator,
        IImageNormalizer imageNormalizer, ILeaseResourceLock resourceLock,
        IOptions<PrivateFileStorageOptions> storageOptions, TimeProvider timeProvider,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var maximumBytes = storageOptions.Value.RoomPhotoMaxBytes;
        if (!request.HasFormContentType || request.ContentLength > maximumBytes + MultipartOverheadAllowance)
            return InvalidPhoto();

        ParsedUpload upload;
        try { upload = await ReadUploadAsync(request, storage, maximumBytes, cancellationToken); }
        catch (InvalidDataException) { return InvalidPhoto(); }

        StagedPrivateFile? normalizedStaged = null;
        long normalizedLength;
        try
        {
            await using (var content = await storage.OpenStagedReadAsync(upload.Staged, cancellationToken))
            {
                var validated = await validator.ValidateAsync(content, upload.FileName, upload.ContentType, cancellationToken);
                if (validated is null || validated.Length != upload.Staged.Size) return InvalidPhoto();
            }

            NormalizedImage normalized;
            await using (var content = await storage.OpenStagedReadAsync(upload.Staged, cancellationToken))
            {
                try { normalized = await imageNormalizer.NormalizeAsync(content, cancellationToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Header validation cannot guarantee the pixel data is decodable.
                    return InvalidPhoto();
                }
            }
            await using (normalized.Content)
            {
                normalizedLength = normalized.Length;
                normalizedStaged = await storage.StageAsync(normalized.Content, maximumBytes, cancellationToken);
            }
        }
        finally { await DiscardSafely(storage, upload.Staged); }

        string storageKey;
        try { storageKey = await storage.CommitAsync(normalizedStaged, cancellationToken); }
        catch
        {
            await DiscardSafely(storage, normalizedStaged);
            throw;
        }

        // Include transaction creation and lock acquisition in compensation: bytes are already committed.
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), cancellationToken);
            if (!await db.Rooms.AnyAsync(x => x.Id == roomId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                await DeleteSafely(storage, storageKey);
                return Results.NotFound();
            }
            var count = await db.RoomPhotos.CountAsync(x => x.RoomId == roomId, cancellationToken);
            if (count >= 8)
            {
                await transaction.RollbackAsync(cancellationToken);
                await DeleteSafely(storage, storageKey);
                return Results.BadRequest(new ApiError("ROOM_PHOTO_LIMIT_REACHED", "Remova uma foto antes de adicionar outra."));
            }
            var now = timeProvider.GetUtcNow();
            var file = PrivateFile.Create(storageKey, "image/webp", normalizedLength, PrivateFilePurposes.RoomPhoto, now);
            var photo = RoomPhoto.Attach(roomId, file.Id, count, count == 0, now);
            db.PrivateFiles.Add(file);
            db.RoomPhotos.Add(photo);
            db.AuditEntries.Add(CreateAudit(context, roomId, "ROOM_PHOTO_UPLOADED", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new RoomPhotoResponse(photo.Id, $"/api/admin/rooms/{roomId}/photos/{photo.Id}",
                photo.SortOrder, photo.IsCover, photo.CreatedAt));
        }
        catch
        {
            await DeleteSafely(storage, storageKey);
            throw;
        }
    }

    public static async Task<IResult> DeleteAsync(Guid roomId, Guid photoId, HttpContext context,
        ApplicationDbContext db, IPrivateFileStorage storage, ILeaseResourceLock resourceLock,
        TimeProvider timeProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        PrivateFile previousFile;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), cancellationToken);
            if (!await db.Rooms.AnyAsync(x => x.Id == roomId, cancellationToken)) return Results.NotFound();
            var photos = await db.RoomPhotos.Where(x => x.RoomId == roomId).OrderBy(x => x.SortOrder)
                .ToListAsync(cancellationToken);
            var photo = photos.SingleOrDefault(x => x.Id == photoId);
            if (photo is null) return Results.NotFound();
            var file = await db.PrivateFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == photo.PrivateFileId, cancellationToken);
            if (file is null || file.Purpose != PrivateFilePurposes.RoomPhoto)
            {
                loggerFactory.CreateLogger("RoomPhoto").LogError(
                    "Room photo unavailable. RoomId={RoomId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
                    roomId, photo.PrivateFileId, context.TraceIdentifier);
                return Results.Json(new ApiError("PHOTO_UNAVAILABLE", "A foto da sala está temporariamente indisponível."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            previousFile = file;
            // Delete the old cover before promoting another one; tracked update order is undefined.
            await db.RoomPhotos.Where(x => x.RoomId == roomId && x.Id == photoId).ExecuteDeleteAsync(cancellationToken);
            db.Entry(photo).State = EntityState.Detached;
            photos.Remove(photo);
            for (var i = 0; i < photos.Count; i++)
            {
                photos[i].Reorder(i);
                if (photo.IsCover) photos[i].SetCover(i == 0);
            }
            db.AuditEntries.Add(CreateAudit(context, roomId, "ROOM_PHOTO_REMOVED", timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await CleanupPreviousFile(previousFile, db, storage, loggerFactory, context);
        return Results.NoContent();
    }

    public static async Task<IResult> ReorderAsync(Guid roomId, ReorderRoomPhotosRequest request,
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), cancellationToken);
        if (!await db.Rooms.AnyAsync(x => x.Id == roomId, cancellationToken)) return Results.NotFound();
        var photos = await db.RoomPhotos.Where(x => x.RoomId == roomId).ToListAsync(cancellationToken);
        var ids = request.OrderedPhotoIds;
        if (ids is null || ids.Count != photos.Count || ids.Distinct().Count() != ids.Count ||
            !photos.Select(x => x.Id).ToHashSet().SetEquals(ids)) return InvalidPhoto();
        var byId = photos.ToDictionary(x => x.Id);
        for (var i = 0; i < ids.Count; i++) byId[ids[i]].Reorder(i);
        db.AuditEntries.Add(CreateAudit(context, roomId, "ROOM_PHOTOS_REORDERED", timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> SetCoverAsync(Guid roomId, Guid photoId, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), cancellationToken);
        if (!await db.Rooms.AnyAsync(x => x.Id == roomId, cancellationToken)) return Results.NotFound();
        if (!await db.RoomPhotos.AnyAsync(x => x.RoomId == roomId && x.Id == photoId, cancellationToken))
            return Results.NotFound();
        await db.RoomPhotos.Where(x => x.RoomId == roomId && x.IsCover)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCover, false), cancellationToken);
        await db.RoomPhotos.Where(x => x.RoomId == roomId && x.Id == photoId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCover, true), cancellationToken);
        db.AuditEntries.Add(CreateAudit(context, roomId, "ROOM_PHOTO_COVER_CHANGED", timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<ParsedUpload> ReadUploadAsync(HttpRequest request, IPrivateFileStorage storage,
        long maximumBytes, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException();
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 128) throw new InvalidDataException();
        var reader = new MultipartReader(boundary, request.Body);
        StagedPrivateFile? staged = null;
        string? fileName = null;
        string? declaredContentType = null;
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (staged is not null ||
                    !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal) ||
                    !(disposition.FileName.HasValue || disposition.FileNameStar.HasValue))
                    throw new InvalidDataException();
                fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                declaredContentType = section.ContentType;
                staged = await storage.StageAsync(section.Body, maximumBytes, cancellationToken);
            }
            if (staged is null) throw new InvalidDataException();
            return new ParsedUpload(staged, fileName, declaredContentType);
        }
        catch
        {
            if (staged is not null) await DiscardSafely(storage, staged);
            throw;
        }
    }

    private static async Task CleanupPreviousFile(PrivateFile previousFile, ApplicationDbContext db,
        IPrivateFileStorage storage, ILoggerFactory loggerFactory, HttpContext context)
    {
        try
        {
            if (!await storage.DeleteAsync(previousFile.StorageKey, CancellationToken.None))
            {
                LogCleanupFailure();
                return;
            }
            db.PrivateFiles.Remove(previousFile);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            LogCleanupFailure();
        }
        void LogCleanupFailure() => loggerFactory.CreateLogger("RoomPhoto").LogError(
            "Room photo cleanup failed. PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
            previousFile.Id, context.TraceIdentifier);
    }

    private static AuditEntry CreateAudit(HttpContext context, Guid roomId, string action, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
        IpAddress = context.Connection.RemoteIpAddress?.ToString(), Action = action, Result = "SUCCEEDED",
        OccurredAt = now, CorrelationId = context.TraceIdentifier, TargetEntityType = "ROOM", TargetEntityId = roomId
    };

    private static async Task DiscardSafely(IPrivateFileStorage storage, StagedPrivateFile staged)
    {
        try { await storage.DiscardAsync(staged, CancellationToken.None); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static async Task DeleteSafely(IPrivateFileStorage storage, string storageKey)
    {
        try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static IResult InvalidPhoto() => Results.BadRequest(new ApiError("INVALID_ROOM_PHOTO", "A foto informada é inválida."));
    private sealed record ParsedUpload(StagedPrivateFile Staged, string? FileName, string? ContentType);
}
