using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Common;
using recepcaototem.Features.Rooms;
using SixLabors.ImageSharp;

namespace GestaoPredio.IntegrationTests;

// Task 4 exercises the mutation service with real multipart requests and scoped dependencies.
// HTTP gallery route authorization/CSRF belongs to Task 6; setup uses the existing protected API.
[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomPhotoTests(ModulesApiFactory factory)
{
    private Guid _roomId;
    private Guid _otherRoomId;
    private string _actorId = "";
    private string _csrf = "";

    [Fact]
    public async Task Upload_normalizes_webp_preserves_first_cover_and_audits_without_storage_keys()
    {
        await PrepareAsync();
        var first = Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        Assert.True(first.IsCover);
        Assert.False(second.IsCover);
        Assert.Equal($"/api/admin/rooms/{_roomId}/photos/{first.Id}", first.PhotoUrl);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IPrivateFileStorage>();
        foreach (var file in await db.PrivateFiles.ToListAsync())
        {
            Assert.Equal(PrivateFilePurposes.RoomPhoto, file.Purpose);
            Assert.Equal("image/webp", file.MimeType);
            await using var stream = await storage.OpenReadAsync(file.StorageKey, default);
            Assert.NotNull(stream);
            Assert.Equal(file.Length, stream.Length);
            Assert.Equal("image/webp", (await Image.DetectFormatAsync(stream)).DefaultMimeType);
            stream.Position = 0;
            using var image = await Image.LoadAsync(stream);
            Assert.Equal(512, image.Width);
            Assert.Equal(512, image.Height);
            Assert.DoesNotContain(file.StorageKey, System.Text.Json.JsonSerializer.Serialize(first));
            Assert.DoesNotContain(file.StorageKey, System.Text.Json.JsonSerializer.Serialize(await db.AuditEntries.ToListAsync()));
        }
        await AssertGalleryAsync([first.Id, second.Id], first.Id);
        await AssertAuditsAsync("ROOM_PHOTO_UPLOADED", 2);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("oversized")]
    [InlineData("undecodable")]
    [InlineData("extra-field")]
    [InlineData("duplicate-file")]
    [InlineData("wrong-field")]
    [InlineData("missing-file")]
    public async Task Invalid_upload_has_no_files_metadata_or_audit(string variant)
    {
        await PrepareAsync();
        var result = await UploadAsync(variant: variant);
        AssertError(result, 400, "INVALID_ROOM_PHOTO");
        await AssertEmptyAsync();
    }

    [Fact]
    public async Task Ninth_photo_is_rejected_without_orphans_or_success_audit()
    {
        await PrepareAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 8; i++) ids.Add(Photo(await UploadAsync()).Id);
        AssertError(await UploadAsync(), 400, "ROOM_PHOTO_LIMIT_REACHED");
        await AssertGalleryAsync(ids, ids[0]);
        await AssertAuditsAsync("ROOM_PHOTO_UPLOADED", 8);
        await AssertFileCountAsync(8);
    }

    [Fact]
    public async Task Delete_noncover_compacts_then_delete_cover_promotes_lowest_then_last_clears_gallery()
    {
        await PrepareAsync();
        var first = Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        var third = Photo(await UploadAsync());
        AssertSuccess(await MutateAsync("delete", second.Id));
        await AssertGalleryAsync([first.Id, third.Id], first.Id);
        await AssertFileCountAsync(2);
        AssertSuccess(await MutateAsync("delete", first.Id));
        await AssertGalleryAsync([third.Id], third.Id);
        AssertSuccess(await MutateAsync("delete", third.Id));
        await AssertGalleryAsync([], null);
        await AssertFileCountAsync(0);
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 3);
    }

    [Fact]
    public async Task Reorder_preserves_cover_and_set_cover_changes_exactly_one()
    {
        await PrepareAsync();
        var first = Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        var third = Photo(await UploadAsync());
        AssertSuccess(await MutateAsync("reorder", ids: [third.Id, first.Id, second.Id]));
        await AssertGalleryAsync([third.Id, first.Id, second.Id], first.Id);
        AssertSuccess(await MutateAsync("cover", second.Id));
        await AssertGalleryAsync([third.Id, first.Id, second.Id], second.Id);
        // Cover deletion must promote by current order, not creation order.
        AssertSuccess(await MutateAsync("delete", second.Id));
        await AssertGalleryAsync([third.Id, first.Id], third.Id);
        await AssertAuditsAsync("ROOM_PHOTOS_REORDERED", 1);
        await AssertAuditsAsync("ROOM_PHOTO_COVER_CHANGED", 1);
    }

    [Fact]
    public async Task Invalid_reorders_and_foreign_photo_mutations_do_not_change_gallery_or_audit()
    {
        await PrepareAsync();
        var first = Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        var foreign = Photo(await UploadAsync(roomId: _otherRoomId));
        foreach (var ids in new IReadOnlyList<Guid>?[] { null, [], [first.Id], [first.Id, first.Id], [first.Id, foreign.Id] })
            AssertError(await MutateAsync("reorder", ids: ids), 400, "INVALID_ROOM_PHOTO");
        Assert.Equal(404, Status(await MutateAsync("delete", foreign.Id)));
        Assert.Equal(404, Status(await MutateAsync("cover", foreign.Id)));
        await AssertGalleryAsync([first.Id, second.Id], first.Id);
        await AssertAuditsAsync("ROOM_PHOTOS_REORDERED", 0);
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 0);
        await AssertAuditsAsync("ROOM_PHOTO_COVER_CHANGED", 0);
    }

    [Fact]
    public async Task Missing_room_returns_404_for_every_mutation_and_cleans_committed_upload()
    {
        await PrepareAsync();
        var absent = Guid.NewGuid();
        Assert.Equal(404, Status(await UploadAsync(roomId: absent)));
        foreach (var operation in new[] { "delete", "cover", "reorder" })
            Assert.Equal(404, Status(await MutateAsync(operation, Guid.NewGuid(), [], absent)));
        await AssertEmptyAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Concurrent_uploads_serialize_cover_order_and_limit(int initialCount)
    {
        await PrepareAsync();
        for (var i = 0; i < initialCount; i++) Photo(await UploadAsync());
        var gate = new AsyncGate();
        var results = await Task.WhenAll(UploadAsync(gate: gate), UploadAsync(gate: gate));
        Assert.Equal(initialCount == 0 ? 2 : 1, results.Count(x => Status(x) == 200));
        Assert.All(results, result => Assert.Contains(Status(result), new[] { 200, 400 }));
        if (initialCount == 7) AssertError(results.Single(x => Status(x) == 400), 400, "ROOM_PHOTO_LIMIT_REACHED");
        var photos = await ReadPhotosAsync();
        Assert.Equal(Math.Min(8, initialCount + 2), photos.Count);
        await AssertGalleryAsync(photos.Select(x => x.Id).ToArray(), photos.Single(x => x.IsCover).Id);
        await AssertFileCountAsync(photos.Count);
        await AssertAuditsAsync("ROOM_PHOTO_UPLOADED", photos.Count);
    }

    [Fact]
    public async Task Concurrent_cover_changes_never_violate_unique_cover_index()
    {
        await PrepareAsync();
        Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        var third = Photo(await UploadAsync());
        var gate = new AsyncGate();
        var results = await Task.WhenAll(MutateAsync("cover", second.Id, gate: gate), MutateAsync("cover", third.Id, gate: gate));
        Assert.All(results, AssertSuccess);
        var photos = await ReadPhotosAsync();
        Assert.Contains(photos.Single(x => x.IsCover).Id, new[] { second.Id, third.Id });
        await AssertAuditsAsync("ROOM_PHOTO_COVER_CHANGED", 2);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("stage-cancel")]
    [InlineData("commit")]
    [InlineData("normalize-cancel")]
    [InlineData("database")]
    [InlineData("lock")]
    public async Task Failures_clean_staging_and_new_storage_without_metadata_or_audit(string failure)
    {
        await PrepareAsync();
        if (failure == "database")
            await Assert.ThrowsAsync<DbUpdateException>(() => UploadAsync(failure: failure));
        else if (failure is "normalize-cancel" or "stage-cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => UploadAsync(failure: failure));
        else
            await Assert.ThrowsAsync<IOException>(() => UploadAsync(failure: failure));
        await AssertEmptyAsync();
    }

    [Theory]
    [InlineData("delete-false")]
    [InlineData("delete-throw")]
    [InlineData("metadata-delete")]
    [InlineData("metadata-provider")]
    public async Task Cleanup_failure_retains_metadata_and_logs_without_reverting_committed_delete(string failure)
    {
        await PrepareAsync();
        var photo = Photo(await UploadAsync());
        var logs = factory.CaptureLogs();
        AssertSuccess(await MutateAsync("delete", photo.Id, failure: failure));
        await AssertGalleryAsync([], null);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PrivateFiles.CountAsync());
        Assert.Equal(failure.StartsWith("metadata-", StringComparison.Ordinal) ? 0 : 1,
            Directory.GetFiles(Path.Combine(factory.PrivateFilesRoot, "files")).Length);
        Assert.Contains("cleanup failed", logs.Text, StringComparison.OrdinalIgnoreCase);
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 1);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("cover")]
    [InlineData("reorder")]
    public async Task Persistence_failure_rolls_back_gallery_changes_and_audit(string operation)
    {
        await PrepareAsync();
        var first = Photo(await UploadAsync());
        var second = Photo(await UploadAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => MutateAsync(operation,
            operation == "delete" ? first.Id : second.Id, [second.Id, first.Id], failure: "database"));
        await AssertGalleryAsync([first.Id, second.Id], first.Id);
        await AssertFileCountAsync(2);
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 0);
        await AssertAuditsAsync("ROOM_PHOTO_COVER_CHANGED", 0);
        await AssertAuditsAsync("ROOM_PHOTOS_REORDERED", 0);
    }

    [Fact]
    public async Task Wrong_file_purpose_returns_safe_unavailable_without_mutation()
    {
        await PrepareAsync();
        var photo = Photo(await UploadAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.PrivateFiles.ExecuteUpdateAsync(set => set.SetProperty(x => x.Purpose, PrivateFilePurposes.ProfessionalPhoto));
        }
        AssertError(await MutateAsync("delete", photo.Id), 503, "PHOTO_UNAVAILABLE");
        await AssertGalleryAsync([photo.Id], photo.Id);
        await AssertFileCountAsync(1);
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 0);
    }

    [Fact]
    public async Task Empty_reorder_is_valid_for_empty_room_and_time_provider_is_used()
    {
        await PrepareAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        AssertSuccess(await MutateAsync("reorder", ids: []));
        var photo = Photo(await UploadAsync());
        Assert.Equal(factory.UtcNow, photo.CreatedAt);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries
            .SingleAsync(x => x.Action == "ROOM_PHOTOS_REORDERED");
        Assert.Equal(factory.UtcNow, audit.OccurredAt);
    }

    [Fact]
    public async Task Normalized_webp_exceeding_valid_limit_returns_invalid_photo_without_artifacts()
    {
        await PrepareAsync();
        const long maximumBytes = 100;
        var bytes = TestImageData.WebP();
        Assert.InRange(bytes.Length, 1, maximumBytes);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var normalizer = scope.ServiceProvider.GetRequiredService<IImageNormalizer>();
            await using var source = new MemoryStream(bytes);
            var normalized = await normalizer.NormalizeAsync(source, default);
            await using (normalized.Content) Assert.True(normalized.Length > maximumBytes);
        }

        AssertError(await UploadAsync(variant: "webp", maximumBytes: maximumBytes), 400, "INVALID_ROOM_PHOTO");
        await AssertEmptyAsync();
    }

    [Fact]
    public async Task Metadata_cleanup_cancellation_is_not_swallowed_after_committed_delete()
    {
        await PrepareAsync();
        var photo = Photo(await UploadAsync());
        await Assert.ThrowsAsync<OperationCanceledException>(() => MutateAsync("delete", photo.Id, failure: "metadata-cancel"));
        await AssertGalleryAsync([], null);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PrivateFiles.CountAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
        await AssertAuditsAsync("ROOM_PHOTO_REMOVED", 1);
    }

    private async Task PrepareAsync()
    {
        await factory.ResetAsync();
        var user = await factory.CreateUserAsync("room-photo@lumis.test", "Valid-Password-123!", [SystemRoles.Gerente]);
        _actorId = user.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, "Valid-Password-123!")).StatusCode);
        _csrf = await factory.GetCsrfTokenAsync();
        _roomId = await CreateRoomAsync("Galeria");
        _otherRoomId = await CreateRoomAsync("Outra sala");
    }

    private async Task<Guid> CreateRoomAsync(string name)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new { name, hourlyRate = 50, dailyRate = 300 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomResponse>())!.Id;
    }

    private DefaultHttpContext Context(IServiceProvider services) => new()
    {
        RequestServices = services,
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _actorId)], "Test"))
    };

    private async Task<IResult> UploadAsync(string variant = "valid", Guid? roomId = null, string? failure = null,
        AsyncGate? gate = null, long? maximumBytes = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = Context(services);
        using var multipart = new MultipartFormDataContent();
        var bytes = variant switch
        {
            "invalid" => new byte[] { 1, 2, 3 },
            "oversized" => new byte[5 * 1024 * 1024 + 1],
            "undecodable" => TestImageData.SniffValidButUndecodableWebP(),
            "webp" => TestImageData.WebP(),
            _ => TestImageData.Png(800, 600)
        };
        var file = new ByteArrayContent(bytes);
        var isWebp = variant is "undecodable" or "webp";
        file.Headers.ContentType = new MediaTypeHeaderValue(isWebp ? "image/webp" : "image/png");
        if (variant != "missing-file") multipart.Add(file, variant == "wrong-field" ? "other" : "file", isWebp ? "photo.webp" : "photo.png");
        if (variant == "extra-field") multipart.Add(new StringContent("unexpected"), "concurrencyToken");
        if (variant == "duplicate-file") multipart.Add(new ByteArrayContent(bytes), "file", "other.png");
        context.Request.Body = await multipart.ReadAsStreamAsync();
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.Headers["X-CSRF-TOKEN"] = _csrf;
        // No Content-Length: the streaming size bound must work independently of the early guard.
        await using var db = CreateDb(failure);
        var storage = new FaultStorage(services.GetRequiredService<IPrivateFileStorage>(), failure);
        ILeaseResourceLock resourceLock = new GestaoPredio.Infrastructure.Leases.PostgreSqlLeaseResourceLock(db);
        if (gate is not null || failure == "lock") resourceLock = new GatedLock(resourceLock, gate, failure == "lock");
        var options = services.GetRequiredService<IOptions<PrivateFileStorageOptions>>();
        if (maximumBytes.HasValue) options = Options.Create(new PrivateFileStorageOptions
        {
            PrivateFilesPath = options.Value.PrivateFilesPath,
            ProfessionalPhotoMaxBytes = options.Value.ProfessionalPhotoMaxBytes,
            RoomPhotoMaxBytes = maximumBytes.Value
        });
        return await RoomPhotoMutation.UploadAsync(roomId ?? _roomId, context.Request, context, db, storage,
            services.GetRequiredService<IProfessionalPhotoValidator>(),
            failure == "normalize-cancel" ? new CancelNormalizer() : services.GetRequiredService<IImageNormalizer>(),
            resourceLock, options,
            services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILoggerFactory>(), default);
    }

    private ApplicationDbContext CreateDb(string? failure)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(factory.ConnectionString);
        if (failure == "database" || failure?.StartsWith("metadata-", StringComparison.Ordinal) == true)
            options.AddInterceptors(new FailureInterceptor(failure));
        return new ApplicationDbContext(options.Options);
    }

    private async Task<IResult> MutateAsync(string operation, Guid photoId = default, IReadOnlyList<Guid>? ids = null,
        Guid? roomId = null, string? failure = null, AsyncGate? gate = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await using var db = CreateDb(failure);
        var context = Context(services);
        ILeaseResourceLock resourceLock = new GestaoPredio.Infrastructure.Leases.PostgreSqlLeaseResourceLock(db);
        if (gate is not null) resourceLock = new GatedLock(resourceLock, gate, false);
        var clock = services.GetRequiredService<TimeProvider>();
        return operation switch
        {
            "delete" => await RoomPhotoMutation.DeleteAsync(roomId ?? _roomId, photoId, context, db,
                new FaultStorage(services.GetRequiredService<IPrivateFileStorage>(), failure), resourceLock, clock,
                services.GetRequiredService<ILoggerFactory>(), default),
            "cover" => await RoomPhotoMutation.SetCoverAsync(roomId ?? _roomId, photoId, context, db, resourceLock, clock, default),
            _ => await RoomPhotoMutation.ReorderAsync(roomId ?? _roomId, new ReorderRoomPhotosRequest(ids), context, db, resourceLock, clock, default)
        };
    }

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 200;
    private static void AssertSuccess(IResult result) => Assert.InRange(Status(result), 200, 299);
    private static RoomPhotoResponse Photo(IResult result)
    {
        Assert.Equal(200, Status(result));
        return Assert.IsType<RoomPhotoResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }
    private static void AssertError(IResult result, int status, string code)
    {
        Assert.Equal(status, Status(result));
        Assert.Equal(code, Assert.IsType<ApiError>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value).Code);
    }
    private async Task<List<RoomPhoto>> ReadPhotosAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RoomPhotos.AsNoTracking()
            .Where(x => x.RoomId == _roomId).OrderBy(x => x.SortOrder).ToListAsync();
    }
    private async Task AssertGalleryAsync(IEnumerable<Guid> orderedIds, Guid? coverId)
    {
        var photos = await ReadPhotosAsync();
        Assert.Equal(orderedIds, photos.Select(x => x.Id));
        Assert.Equal(Enumerable.Range(0, photos.Count), photos.Select(x => x.SortOrder));
        Assert.Equal(coverId is null ? 0 : 1, photos.Count(x => x.IsCover));
        if (coverId is not null) Assert.Equal(coverId, photos.Single(x => x.IsCover).Id);
    }
    private async Task AssertAuditsAsync(string action, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var audits = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.Where(x => x.Action == action).ToListAsync();
        Assert.Equal(count, audits.Count);
        Assert.All(audits, audit => { Assert.Equal(_actorId, audit.ActorUserId); Assert.Equal("SUCCEEDED", audit.Result); });
    }
    private async Task AssertFileCountAsync(int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(count, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PrivateFiles.CountAsync());
        Assert.Equal(count, Directory.GetFiles(Path.Combine(factory.PrivateFilesRoot, "files")).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.PrivateFilesRoot, ".staging")));
    }
    private async Task AssertEmptyAsync()
    {
        await AssertFileCountAsync(0);
        await AssertGalleryAsync([], null);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.CountAsync(x => x.Action.StartsWith("ROOM_PHOTO")));
    }

    private sealed class FailureInterceptor(string failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (failure == "database" || eventData.Context!.ChangeTracker.Entries<PrivateFile>().Any(x => x.State == EntityState.Deleted))
                throw failure switch
                {
                    "metadata-provider" => new Npgsql.NpgsqlException("Injected provider connection failure"),
                    "metadata-cancel" => new OperationCanceledException(),
                    _ => new DbUpdateException("Injected persistence failure")
                };
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class CancelNormalizer : IImageNormalizer
    {
        public Task<NormalizedImage> NormalizeAsync(Stream source, CancellationToken cancellationToken) => throw new OperationCanceledException();
    }
    private sealed class AsyncGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.SetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
    private sealed class GatedLock(ILeaseResourceLock inner, AsyncGate? gate, bool fail) : ILeaseResourceLock
    {
        public async Task AcquireAsync(LeaseResourceLockRequest request, CancellationToken cancellationToken)
        {
            if (fail) throw new IOException("Injected lock failure");
            if (gate is not null) await gate.ArriveAsync();
            await inner.AcquireAsync(request, cancellationToken);
        }
    }
    private sealed class FaultStorage(IPrivateFileStorage inner, string? failure) : IPrivateFileStorage
    {
        private int _stages;
        public Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _stages) == 2)
            {
                if (failure == "stage") throw new IOException("Injected stage failure");
                if (failure == "stage-cancel") throw new OperationCanceledException();
            }
            return inner.StageAsync(source, maximumBytes, ct);
        }
        public Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct) =>
            failure == "commit" ? throw new IOException("Injected commit failure") : inner.CommitAsync(staged, ct);
        public Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct) => inner.OpenStagedReadAsync(staged, ct);
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);
        public Task<bool> DeleteAsync(string key, CancellationToken ct) => failure switch
        {
            "delete-false" => Task.FromResult(false),
            "delete-throw" => throw new IOException("Injected delete failure"),
            _ => inner.DeleteAsync(key, ct)
        };
        public Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct) => inner.DiscardAsync(staged, ct);
    }
}
