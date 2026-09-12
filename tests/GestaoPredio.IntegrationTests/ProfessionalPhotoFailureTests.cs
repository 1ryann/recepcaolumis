using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Common;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalPhotoFailureTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Metadata_storage_inconsistency_returns_safe_503_without_repair_side_effect()
    {
        await factory.ResetAsync();
        await LoginAsync(factory.Client, "photo-unavailable@lumis.test");
        var professional = Professional.Create("Ana", "Fisio", "65999999999", DateTimeOffset.UtcNow);
        var metadata = PrivateFile.Create(Guid.NewGuid().ToString("N"), "image/png", 10,
            PrivateFilePurposes.ProfessionalPhoto, DateTimeOffset.UtcNow);
        professional.SetPhoto(metadata.Id, DateTimeOffset.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PrivateFiles.Add(metadata);
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }

        var response = await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}/photo");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("PHOTO_UNAVAILABLE", await response.Content.ReadAsStringAsync());
        await using var verify = factory.Services.CreateAsyncScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(metadata.Id, (await dbVerify.Professionals.SingleAsync()).PhotoFileId);
    }

    [Fact]
    public async Task Concurrent_change_after_file_commit_removes_new_bytes_and_writes_no_success_audit()
    {
        await factory.ResetAsync();
        var admin = await factory.CreateUserAsync("photo-race@lumis.test", Password, [SystemRoles.Administrador]);
        var professional = Professional.Create("Race", "Fisio", "65999999999", DateTimeOffset.UtcNow);
        Guid previousFileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IPrivateFileStorage>();
            await using var source = new MemoryStream(TestImageData.Png());
            var staged = await storage.StageAsync(source, 5 * 1024 * 1024, CancellationToken.None);
            var key = await storage.CommitAsync(staged, CancellationToken.None);
            var previousFile = PrivateFile.Create(key, "image/png", TestImageData.Png().Length,
                PrivateFilePurposes.ProfessionalPhoto, DateTimeOffset.UtcNow);
            previousFileId = previousFile.Id;
            professional.SetPhoto(previousFile.Id, DateTimeOffset.UtcNow);
            db.PrivateFiles.Add(previousFile);
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            token = ConcurrencyToken.Encode((await db.Professionals.AsNoTracking().SingleAsync()).Version);
        }

        await using var child = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<PrivateFileStorageOptions>>().Value;
                return new ConcurrentMutationStorage(new FileSystemPrivateFileStorage(options),
                    provider.GetRequiredService<IServiceScopeFactory>(), professional.Id);
            });
        }));
        using var client = child.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = admin.Email, password = Password })
        })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        }
        csrf = await GetCsrfAsync(client);

        var response = await ProfessionalPhotoTests.PutPhotoAsync(client, professional.Id, token,
            TestImageData.Png(), "race.png", "image/png", csrf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(previousFileId, (await verifyDb.Professionals.AsNoTracking().SingleAsync()).PhotoFileId);
        Assert.Equal(previousFileId, (await verifyDb.PrivateFiles.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(0, await verifyDb.AuditEntries.CountAsync(x => x.Action.StartsWith("PROFESSIONAL_PHOTO_")));
    }

    [Fact]
    public async Task Old_byte_cleanup_failure_keeps_the_committed_replacement_and_old_metadata()
    {
        await factory.ResetAsync();
        var admin = await factory.CreateUserAsync("photo-cleanup@lumis.test", Password, [SystemRoles.Administrador]);
        var professional = Professional.Create("Cleanup", "Fisio", "65999999999", DateTimeOffset.UtcNow);
        string previousKey;
        Guid previousFileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IPrivateFileStorage>();
            await using var source = new MemoryStream(TestImageData.Png());
            var staged = await storage.StageAsync(source, 5 * 1024 * 1024, CancellationToken.None);
            previousKey = await storage.CommitAsync(staged, CancellationToken.None);
            var previous = PrivateFile.Create(previousKey, "image/png", TestImageData.Png().Length,
                PrivateFilePurposes.ProfessionalPhoto, DateTimeOffset.UtcNow);
            previousFileId = previous.Id;
            professional.SetPhoto(previous.Id, DateTimeOffset.UtcNow);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(previous, professional);
            await db.SaveChangesAsync();
        }
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
            token = ConcurrencyToken.Encode((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Professionals.AsNoTracking().SingleAsync()).Version);

        await using var child = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<PrivateFileStorageOptions>>().Value;
                return new DeleteFailureStorage(new FileSystemPrivateFileStorage(options), previousKey);
            });
        }));
        using var client = child.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = admin.Email, password = Password })
        })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        }

        var response = await ProfessionalPhotoTests.PutPhotoAsync(client, professional.Id, token,
            TestImageData.Png(), "replacement.png", "image/png", await GetCsrfAsync(client));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = factory.Services.CreateAsyncScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var current = await dbVerify.Professionals.AsNoTracking().SingleAsync();
        Assert.NotEqual(previousFileId, current.PhotoFileId);
        Assert.Equal(2, await dbVerify.PrivateFiles.CountAsync());
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")).Count());
        Assert.Equal(1, await dbVerify.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_PHOTO_REPLACED"));
    }

    [Fact]
    public async Task Storage_failure_while_staging_the_normalized_image_is_not_reported_as_an_invalid_photo()
    {
        await factory.ResetAsync();
        var admin = await factory.CreateUserAsync("photo-restage-failure@lumis.test", Password, [SystemRoles.Administrador]);
        var professional = Professional.Create("Restage", "Fisio", "65999999999", DateTimeOffset.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
            token = ConcurrencyToken.Encode((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Professionals.AsNoTracking().SingleAsync()).Version);

        await using var child = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<PrivateFileStorageOptions>>().Value;
                return new ThrowOnSecondStageStorage(new FileSystemPrivateFileStorage(options));
            });
        }));
        using var client = child.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = admin.Email, password = Password })
        })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        }

        var response = await ProfessionalPhotoTests.PutPhotoAsync(client, professional.Id, token,
            TestImageData.Png(), "replacement.png", "image/png", await GetCsrfAsync(client));

        // A disk-full/permission/IO failure while re-staging the normalized image is not the
        // caller's fault: it must not be misreported as INVALID_PROFESSIONAL_PHOTO (400).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("INVALID_PROFESSIONAL_PHOTO", body);
        await using var verify = factory.Services.CreateAsyncScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await dbVerify.Professionals.AsNoTracking().SingleAsync()).PhotoFileId);
    }

    private async Task LoginAsync(HttpClient client, string email)
    {
        await factory.CreateUserAsync(email, Password, [SystemRoles.Administrador]);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

    private sealed record CsrfPayload(string Token);

    private sealed class ConcurrentMutationStorage(
        IPrivateFileStorage inner,
        IServiceScopeFactory scopes,
        Guid professionalId) : IPrivateFileStorage
    {
        public Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct) =>
            inner.StageAsync(source, maximumBytes, ct);
        public async Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct)
        {
            var key = await inner.CommitAsync(staged, ct);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.Professionals.SingleAsync(x => x.Id == professionalId, ct);
            entity.Update(entity.Name, entity.Profession, entity.WhatsApp, DateTimeOffset.UtcNow.AddSeconds(1));
            await db.SaveChangesAsync(ct);
            return key;
        }
        public Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct) =>
            inner.OpenStagedReadAsync(staged, ct);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct) => inner.OpenReadAsync(storageKey, ct);
        public Task<bool> DeleteAsync(string storageKey, CancellationToken ct) => inner.DeleteAsync(storageKey, ct);
        public Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct) => inner.DiscardAsync(staged, ct);
    }

    private sealed class DeleteFailureStorage(IPrivateFileStorage inner, string failedKey) : IPrivateFileStorage
    {
        public Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct) =>
            inner.StageAsync(source, maximumBytes, ct);
        public Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct) => inner.CommitAsync(staged, ct);
        public Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct) =>
            inner.OpenStagedReadAsync(staged, ct);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct) => inner.OpenReadAsync(storageKey, ct);
        public Task<bool> DeleteAsync(string storageKey, CancellationToken ct) =>
            storageKey == failedKey ? Task.FromResult(false) : inner.DeleteAsync(storageKey, ct);
        public Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct) => inner.DiscardAsync(staged, ct);
    }

    // The mutation stages the raw upload once (in ReadUploadAsync) and stages again after
    // normalization; this simulates an IO failure hitting only that second, post-normalization stage.
    private sealed class ThrowOnSecondStageStorage(IPrivateFileStorage inner) : IPrivateFileStorage
    {
        private int _stageCalls;
        public Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct) =>
            Interlocked.Increment(ref _stageCalls) == 2
                ? throw new IOException("Simulated disk failure while staging the normalized image.")
                : inner.StageAsync(source, maximumBytes, ct);
        public Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct) => inner.CommitAsync(staged, ct);
        public Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct) =>
            inner.OpenStagedReadAsync(staged, ct);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct) => inner.OpenReadAsync(storageKey, ct);
        public Task<bool> DeleteAsync(string storageKey, CancellationToken ct) => inner.DeleteAsync(storageKey, ct);
        public Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct) => inner.DiscardAsync(staged, ct);
    }
}
