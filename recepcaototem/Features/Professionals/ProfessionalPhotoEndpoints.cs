using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalPhotoDeleteRequest(string? ConcurrencyToken) : IStrictModuleRequest;

public static class ProfessionalPhotoEndpoints
{
    private const string InvalidPhotoCode = "INVALID_PROFESSIONAL_PHOTO";
    private const string PhotoUnavailableCode = "PHOTO_UNAVAILABLE";
    private const long MultipartOverheadAllowance = 64 * 1024;

    public static IEndpointRouteBuilder MapProfessionalPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/professionals").RequireAuthorization("Operations");
        group.MapGet("/{id:guid}/photo", Get);
        group.MapPut("/{id:guid}/photo", Put).AddEndpointFilter<AntiforgeryFilter>();
        group.MapDelete("/{id:guid}/photo", Delete).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> Get(
        Guid id,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null || professional.PhotoFileId is null) return Results.NotFound();

        var metadata = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == professional.PhotoFileId, cancellationToken);
        if (metadata is null || !string.Equals(metadata.Purpose, PrivateFilePurposes.ProfessionalPhoto,
                StringComparison.Ordinal))
            return PhotoUnavailable(loggerFactory, context, professional.Id, professional.PhotoFileId.Value);

        Stream? stream;
        try
        {
            stream = await storage.OpenReadAsync(metadata.StorageKey, cancellationToken);
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
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(stream, metadata.MimeType, enableRangeProcessing: false);
    }

    private static async Task<IResult> Put(
        Guid id,
        HttpRequest request,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        IProfessionalPhotoValidator validator,
        IOptions<PrivateFileStorageOptions> storageOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType ||
            request.ContentLength > storageOptions.Value.ProfessionalPhotoMaxBytes + MultipartOverheadAllowance)
            return InvalidPhoto();

        ParsedUpload upload;
        try
        {
            upload = await ReadUploadAsync(request, storage, storageOptions.Value.ProfessionalPhotoMaxBytes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidPhotoRequestException or InvalidDataException)
        {
            return InvalidPhoto();
        }

        if (!ConcurrencyToken.TryDecode(upload.ConcurrencyToken, out var expectedVersion))
        {
            await DiscardSafely(storage, upload.Staged);
            return ProfessionalEndpoints.InvalidToken();
        }

        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null)
        {
            await DiscardSafely(storage, upload.Staged);
            return Results.NotFound();
        }
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion))
        {
            await DiscardSafely(storage, upload.Staged);
            return ProfessionalEndpoints.Modified();
        }

        ValidatedImage? validated;
        try
        {
            await using var stagedContent = await storage.OpenStagedReadAsync(upload.Staged, cancellationToken);
            validated = await validator.ValidateAsync(stagedContent, upload.FileName, upload.ContentType,
                cancellationToken);
        }
        catch
        {
            await DiscardSafely(storage, upload.Staged);
            throw;
        }

        if (validated is null || validated.Length != upload.Staged.Size)
        {
            await DiscardSafely(storage, upload.Staged);
            return InvalidPhoto();
        }

        string newStorageKey;
        try
        {
            newStorageKey = await storage.CommitAsync(upload.Staged, cancellationToken);
        }
        catch
        {
            await DiscardSafely(storage, upload.Staged);
            throw;
        }

        PrivateFile? previousFile = null;
        var previousFileId = professional.PhotoFileId;
        if (previousFileId is not null)
        {
            previousFile = await db.PrivateFiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == previousFileId, cancellationToken);
            if (previousFile is null || !string.Equals(previousFile.Purpose, PrivateFilePurposes.ProfessionalPhoto,
                    StringComparison.Ordinal))
            {
                await DeleteSafely(storage, newStorageKey);
                return PhotoUnavailable(loggerFactory, context, professional.Id, previousFileId.Value);
            }
        }

        var now = timeProvider.GetUtcNow();
        var newFile = PrivateFile.Create(newStorageKey, validated.MimeType, validated.Length,
            PrivateFilePurposes.ProfessionalPhoto, now);
        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        professional.SetPhoto(newFile.Id, now);
        db.PrivateFiles.Add(newFile);
        db.AuditEntries.Add(ProfessionalEndpoints.CreateAudit(context, professional.Id,
            previousFileId is null ? "PROFESSIONAL_PHOTO_UPLOADED" : "PROFESSIONAL_PHOTO_REPLACED", now));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackSafely(transaction);
            await DeleteSafely(storage, newStorageKey);
            return ProfessionalEndpoints.Modified();
        }
        catch
        {
            await RollbackSafely(transaction);
            await DeleteSafely(storage, newStorageKey);
            throw;
        }

        if (previousFile is not null)
            await CleanupPreviousFile(previousFile, db, storage, loggerFactory, context);
        return Results.Ok(professional.ToResponse());
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
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return ProfessionalEndpoints.InvalidToken();

        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (professional is null) return Results.NotFound();
        if (!professional.RowVersion.AsSpan().SequenceEqual(expectedVersion)) return ProfessionalEndpoints.Modified();
        if (professional.PhotoFileId is null) return Results.Ok(professional.ToResponse());

        var previousFile = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == professional.PhotoFileId, cancellationToken);
        if (previousFile is null || !string.Equals(previousFile.Purpose, PrivateFilePurposes.ProfessionalPhoto,
                StringComparison.Ordinal))
            return PhotoUnavailable(loggerFactory, context, professional.Id, professional.PhotoFileId.Value);

        db.Entry(professional).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        var now = timeProvider.GetUtcNow();
        professional.RemovePhoto(now);
        db.AuditEntries.Add(ProfessionalEndpoints.CreateAudit(context, professional.Id,
            "PROFESSIONAL_PHOTO_REMOVED", now));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackSafely(transaction);
            return ProfessionalEndpoints.Modified();
        }

        await CleanupPreviousFile(previousFile, db, storage, loggerFactory, context);
        return Results.Ok(professional.ToResponse());
    }

    private static async Task CleanupPreviousFile(
        PrivateFile previousFile,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        ILoggerFactory loggerFactory,
        HttpContext context)
    {
        try
        {
            if (!await storage.DeleteAsync(previousFile.StorageKey, CancellationToken.None))
            {
                LogCleanupFailure(loggerFactory, context, previousFile.Id);
                return;
            }

            db.PrivateFiles.Remove(previousFile);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            LogCleanupFailure(loggerFactory, context, previousFile.Id);
        }
    }

    private static async Task<ParsedUpload> ReadUploadAsync(
        HttpRequest request,
        IPrivateFileStorage storage,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
            throw new InvalidPhotoRequestException();
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 128)
            throw new InvalidPhotoRequestException();

        var reader = new MultipartReader(boundary, request.Body);
        StagedPrivateFile? staged = null;
        string? token = null;
        string? fileName = null;
        string? declaredContentType = null;
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidPhotoRequestException();

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                var hasFileName = disposition.FileName.HasValue || disposition.FileNameStar.HasValue;
                if (hasFileName)
                {
                    if (staged is not null || !string.Equals(name, "file", StringComparison.Ordinal))
                        throw new InvalidPhotoRequestException();
                    fileName = HeaderUtilities.RemoveQuotes(
                        disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                    declaredContentType = section.ContentType;
                    try
                    {
                        staged = await storage.StageAsync(section.Body, maximumBytes, cancellationToken);
                    }
                    catch (InvalidDataException)
                    {
                        throw new InvalidPhotoRequestException();
                    }
                }
                else if (string.Equals(name, "concurrencyToken", StringComparison.Ordinal) && token is null)
                {
                    using var textReader = new StreamReader(section.Body);
                    token = await textReader.ReadToEndAsync(cancellationToken);
                    if (token.Length > 512) throw new InvalidPhotoRequestException();
                }
                else
                {
                    throw new InvalidPhotoRequestException();
                }
            }

            if (staged is null || token is null) throw new InvalidPhotoRequestException();
            return new ParsedUpload(staged, token, fileName, declaredContentType);
        }
        catch
        {
            if (staged is not null) await DiscardSafely(storage, staged);
            throw;
        }
    }

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

    private static async Task RollbackSafely(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
    }

    private static IResult PhotoUnavailable(
        ILoggerFactory loggerFactory,
        HttpContext context,
        Guid professionalId,
        Guid privateFileId)
    {
        loggerFactory.CreateLogger("ProfessionalPhoto")
            .LogError("Professional photo unavailable. ProfessionalId={ProfessionalId} PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
                professionalId, privateFileId, context.TraceIdentifier);
        return Results.Json(new ApiError(PhotoUnavailableCode, "A foto do profissional está temporariamente indisponível."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static void LogCleanupFailure(
        ILoggerFactory loggerFactory,
        HttpContext context,
        Guid privateFileId) =>
        loggerFactory.CreateLogger("ProfessionalPhoto")
            .LogError("Professional photo cleanup failed. PrivateFileId={PrivateFileId} CorrelationId={CorrelationId}",
                privateFileId, context.TraceIdentifier);

    private static IResult InvalidPhoto() => Results.BadRequest(new ApiError(
        InvalidPhotoCode, "A foto informada é inválida."));

    private sealed record ParsedUpload(
        StagedPrivateFile Staged,
        string? ConcurrencyToken,
        string? FileName,
        string? ContentType);

    private sealed class InvalidPhotoRequestException : Exception;
}
