using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

internal sealed class PhotoMutationOutcome
{
    public bool Succeeded { get; }
    public IResult? ErrorResult { get; }
    private PhotoMutationOutcome(bool succeeded, IResult? errorResult) { Succeeded = succeeded; ErrorResult = errorResult; }
    public static PhotoMutationOutcome Ok() => new(true, null);
    public static PhotoMutationOutcome Failed(IResult errorResult) => new(false, errorResult);
}

internal static class ProfessionalPhotoMutation
{
    private const string InvalidPhotoCode = "INVALID_PROFESSIONAL_PHOTO";
    private const string PhotoUnavailableCode = "PHOTO_UNAVAILABLE";
    private const long MultipartOverheadAllowance = 64 * 1024;

    internal static async Task<PhotoMutationOutcome> PutAsync(
        Professional professional,
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
            return PhotoMutationOutcome.Failed(InvalidPhoto());

        ParsedUpload upload;
        try
        {
            upload = await ReadUploadAsync(request, storage, storageOptions.Value.ProfessionalPhotoMaxBytes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidPhotoRequestException or InvalidDataException)
        {
            return PhotoMutationOutcome.Failed(InvalidPhoto());
        }

        if (!ConcurrencyToken.TryDecode(upload.ConcurrencyToken, out var expectedVersion))
        {
            await DiscardSafely(storage, upload.Staged);
            return PhotoMutationOutcome.Failed(ProfessionalEndpoints.InvalidToken());
        }

        if (professional.Version != expectedVersion)
        {
            await DiscardSafely(storage, upload.Staged);
            return PhotoMutationOutcome.Failed(ProfessionalEndpoints.Modified());
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
            return PhotoMutationOutcome.Failed(InvalidPhoto());
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
                return PhotoMutationOutcome.Failed(
                    PhotoUnavailable(loggerFactory, context, professional.Id, previousFileId.Value));
            }
        }

        var now = timeProvider.GetUtcNow();
        var newFile = PrivateFile.Create(newStorageKey, validated.MimeType, validated.Length,
            PrivateFilePurposes.ProfessionalPhoto, now);
        db.Entry(professional).Property(x => x.Version).OriginalValue = expectedVersion;
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
            return PhotoMutationOutcome.Failed(ProfessionalEndpoints.Modified());
        }
        catch
        {
            await RollbackSafely(transaction);
            await DeleteSafely(storage, newStorageKey);
            throw;
        }

        if (previousFile is not null)
            await CleanupPreviousFile(previousFile, db, storage, loggerFactory, context);
        return PhotoMutationOutcome.Ok();
    }

    internal static async Task<PhotoMutationOutcome> DeleteAsync(
        Professional professional,
        string? concurrencyTokenValue,
        HttpContext context,
        ApplicationDbContext db,
        IPrivateFileStorage storage,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(concurrencyTokenValue, out var expectedVersion))
            return PhotoMutationOutcome.Failed(ProfessionalEndpoints.InvalidToken());

        if (professional.Version != expectedVersion) return PhotoMutationOutcome.Failed(ProfessionalEndpoints.Modified());
        if (professional.PhotoFileId is null) return PhotoMutationOutcome.Ok();

        var previousFile = await db.PrivateFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == professional.PhotoFileId, cancellationToken);
        if (previousFile is null || !string.Equals(previousFile.Purpose, PrivateFilePurposes.ProfessionalPhoto,
                StringComparison.Ordinal))
            return PhotoMutationOutcome.Failed(
                PhotoUnavailable(loggerFactory, context, professional.Id, professional.PhotoFileId.Value));

        db.Entry(professional).Property(x => x.Version).OriginalValue = expectedVersion;
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
            return PhotoMutationOutcome.Failed(ProfessionalEndpoints.Modified());
        }

        await CleanupPreviousFile(previousFile, db, storage, loggerFactory, context);
        return PhotoMutationOutcome.Ok();
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
