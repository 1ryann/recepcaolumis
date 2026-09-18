using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Files;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using recepcaototem.Features.Common;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalSelfPhotoTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Post_me_photo_uploads_and_returns_versioned_photo_url()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync("self-photo-upload@lumis.test");
        var token = await GetConcurrencyTokenAsync(factory.Client);

        var response = await UploadAsync(factory.Client, token, TestImageData.Png(), "photo.png", "image/png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal("/api/professional/me/photo", body.GetProperty("photoUrl").GetString());
        Assert.NotEqual(token, body.GetProperty("concurrencyToken").GetString());

        var get = await factory.Client.GetAsync("/api/professional/me/photo");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Delete_me_photo_removes_photo()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync("self-photo-delete@lumis.test");
        var uploadToken = await GetConcurrencyTokenAsync(factory.Client);
        var uploaded = await UploadAsync(factory.Client, uploadToken, TestImageData.Png(), "photo.png", "image/png");
        var afterUploadToken = (await uploaded.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyToken").GetString()!;

        var deleteResponse = await DeleteAsync(factory.Client, afterUploadToken);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var body = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("hasPhoto").GetBoolean());
        Assert.Null(body.GetProperty("photoUrl").GetString() is { } url && url.Length > 0 ? (string?)url : null);

        var get = await factory.Client.GetAsync("/api/professional/me/photo");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Post_me_photo_without_authentication_returns_unauthorized()
    {
        await factory.ResetAsync();
        var csrf = await factory.GetCsrfTokenAsync();
        var response = await UploadAsync(factory.Client, "irrelevant", TestImageData.Png(), "photo.png", "image/png",
            csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_me_photo_without_authentication_returns_unauthorized()
    {
        await factory.ResetAsync();
        var response = await DeleteAsync(factory.Client, "irrelevant");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_me_photo_without_professional_link_returns_not_found()
    {
        await factory.ResetAsync();
        var user = await factory.CreateUserAsync("no-link@lumis.test", Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
        var csrf = await factory.GetCsrfTokenAsync();

        var response = await UploadAsync(factory.Client, "irrelevant", TestImageData.Png(), "photo.png", "image/png",
            csrf);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PROFESSIONAL_PROFILE_NOT_LINKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_me_photo_without_professional_link_returns_not_found()
    {
        await factory.ResetAsync();
        var user = await factory.CreateUserAsync("no-link-delete@lumis.test", Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

        var response = await DeleteAsync(factory.Client, "irrelevant");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PROFESSIONAL_PROFILE_NOT_LINKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Oversized_upload_is_rejected_as_invalid_photo()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync("self-photo-large@lumis.test");
        var token = await GetConcurrencyTokenAsync(factory.Client);
        var bytes = new byte[(5 * 1024 * 1024) + 1];
        TestImageData.Png().CopyTo(bytes, 0);

        var response = await UploadAsync(factory.Client, token, bytes, "large.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_PROFESSIONAL_PHOTO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Corrupted_magic_bytes_are_rejected_as_invalid_photo()
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync("self-photo-corrupt@lumis.test");
        var token = await GetConcurrencyTokenAsync(factory.Client);

        var response = await UploadAsync(factory.Client, token, [137, 80, 78], "photo.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_PROFESSIONAL_PHOTO", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("jpeg", "photo.jpg", "image/jpeg")]
    [InlineData("png", "photo.png", "image/png")]
    [InlineData("webp", "photo.webp", "image/webp")]
    public async Task All_approved_formats_are_accepted(string format, string filename, string mime)
    {
        await factory.ResetAsync();
        await CreateLinkedProfessionalAsync($"self-photo-{format}@lumis.test");
        var token = await GetConcurrencyTokenAsync(factory.Client);
        var bytes = format switch
        {
            "jpeg" => TestImageData.Jpeg(),
            "png" => TestImageData.Png(),
            "webp" => TestImageData.WebP(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        var response = await UploadAsync(factory.Client, token, bytes, filename, mime);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_photo_changes_the_photo_file_id_and_cleans_the_old_one()
    {
        await factory.ResetAsync();
        var professional = await CreateLinkedProfessionalAsync("self-photo-replace@lumis.test");
        var firstToken = await GetConcurrencyTokenAsync(factory.Client);
        var first = await UploadAsync(factory.Client, firstToken, TestImageData.Png(), "one.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        Guid firstPhotoFileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            firstPhotoFileId = (await db.Professionals.AsNoTracking()
                .SingleAsync(p => p.Id == professional.Id)).PhotoFileId!.Value;
        }

        var secondToken = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyToken").GetString()!;
        var second = await UploadAsync(factory.Client, secondToken, TestImageData.Png(), "two.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await verifyDb.Professionals.AsNoTracking().SingleAsync(p => p.Id == professional.Id);
        Assert.NotEqual(firstPhotoFileId, updated.PhotoFileId);
        Assert.Equal(1, await verifyDb.PrivateFiles.CountAsync());
    }

    [Fact]
    public async Task Professional_a_cannot_alter_professional_b_photo()
    {
        await factory.ResetAsync();
        var professionalA = await CreateLinkedProfessionalAsync("self-photo-a@lumis.test");
        var tokenA = await GetConcurrencyTokenAsync(factory.Client);
        var uploadA = await UploadAsync(factory.Client, tokenA, TestImageData.Png(), "a.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, uploadA.StatusCode);

        await using var childFactory = factory.WithWebHostBuilder(_ => { });
        using var clientB = childFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var userB = await factory.CreateUserAsync("self-photo-b@lumis.test", Password, [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var professionalB = Professional.Create("Beto", "Fisio", "+5511988887777", now);
        professionalB.LinkUser(userB.Id, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professionalB);
            await db.SaveChangesAsync();
        }
        var csrfB = await GetCsrfAsync(clientB);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
               { Content = JsonContent.Create(new { email = userB.Email, password = Password }) })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrfB);
            Assert.Equal(HttpStatusCode.NoContent, (await clientB.SendAsync(login)).StatusCode);
        }
        var getB = await clientB.GetAsync("/api/professional/me");
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        var bodyB = await getB.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(bodyB.GetProperty("hasPhoto").GetBoolean());

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshedA = await verifyDb.Professionals.AsNoTracking().SingleAsync(p => p.Id == professionalA.Id);
        Assert.NotNull(refreshedA.PhotoFileId);
        var refreshedB = await verifyDb.Professionals.AsNoTracking().SingleAsync(p => p.Id == professionalB.Id);
        Assert.Null(refreshedB.PhotoFileId);
    }

    [Fact]
    public async Task Rate_limiter_returns_429_once_the_identifier_limit_is_exhausted()
    {
        await factory.ResetAsync();
        using var childFactory = factory.WithConfig(("RateLimiting:PhotoUploadIdentifierPermitLimit", "1"));
        var user = await factory.CreateUserAsync("self-photo-ratelimit@lumis.test", Password, [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var professional = Professional.Create("Rate", "Fisio", "+5511977776666", now);
        professional.LinkUser(user.Id, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        var csrf = await GetCsrfAsync(childFactory.Client);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
               { Content = JsonContent.Create(new { email = user.Email, password = Password }) })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await childFactory.Client.SendAsync(login)).StatusCode);
        }

        var token = await GetConcurrencyTokenAsync(childFactory.Client);
        var first = await UploadAsync(childFactory.Client, token, TestImageData.Png(), "one.png", "image/png");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondToken = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyToken").GetString()!;
        var second = await UploadAsync(childFactory.Client, secondToken, TestImageData.Png(), "two.png", "image/png");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Mid_flight_storage_failure_preserves_the_previous_photo()
    {
        await factory.ResetAsync();
        var user = await factory.CreateUserAsync("self-photo-midflight@lumis.test", Password, [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var professional = Professional.Create("Mid", "Fisio", "+5511966665555", now);
        professional.LinkUser(user.Id, now);
        Guid previousFileId;
        string previousKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IPrivateFileStorage>();
            await using var source = new MemoryStream(TestImageData.Png());
            var staged = await storage.StageAsync(source, 5 * 1024 * 1024, CancellationToken.None);
            previousKey = await storage.CommitAsync(staged, CancellationToken.None);
            var previousFile = PrivateFile.Create(previousKey, "image/webp", TestImageData.Png().Length,
                PrivateFilePurposes.ProfessionalPhoto, now);
            previousFileId = previousFile.Id;
            professional.SetPhoto(previousFile.Id, now);
            db.PrivateFiles.Add(previousFile);
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
            token = ConcurrencyToken.Encode((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Professionals.AsNoTracking().SingleAsync(p => p.Id == professional.Id)).Version);

        await using var child = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrivateFileStorage>();
            services.AddSingleton<IPrivateFileStorage>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<PrivateFileStorageOptions>>().Value;
                return new ThrowOnCommitStorage(new FileSystemPrivateFileStorage(options));
            });
        }));
        using var client = child.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
               { Content = JsonContent.Create(new { email = user.Email, password = Password }) })
        {
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        }

        var response = await UploadAsync(client, token, TestImageData.Png(), "replacement.png", "image/png",
            await GetCsrfAsync(client));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var verify = factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var current = await verifyDb.Professionals.AsNoTracking().SingleAsync(p => p.Id == professional.Id);
        Assert.Equal(previousFileId, current.PhotoFileId);
        Assert.Equal(1, await verifyDb.PrivateFiles.CountAsync());
        Assert.Equal(previousKey, (await verifyDb.PrivateFiles.AsNoTracking().SingleAsync()).StorageKey);
    }

    private async Task<Professional> CreateLinkedProfessionalAsync(string email)
    {
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var professional = Professional.Create("Ana Souza", "Fisioterapia", "+5511999999999", now);
        professional.LinkUser(user.Id, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
        return professional;
    }

    private static async Task<string> GetConcurrencyTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/professional/me");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("concurrencyToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, byte[] bytes,
        string filename, string mime, string? csrf = null)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token), "concurrencyToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        multipart.Add(file, "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/professional/me/photo")
            { Content = multipart };
        request.Headers.Add("X-CSRF-TOKEN", csrf ?? await GetCsrfAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? concurrencyToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/professional/me/photo")
        {
            Content = JsonContent.Create(new { concurrencyToken })
        };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<CsrfPayload>())!.Token;

    private sealed record CsrfPayload(string Token);

    private sealed class ThrowOnCommitStorage(IPrivateFileStorage inner) : IPrivateFileStorage
    {
        public Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct) =>
            inner.StageAsync(source, maximumBytes, ct);
        public Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct) =>
            throw new IOException("Simulated mid-flight storage failure.");
        public Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct) =>
            inner.OpenStagedReadAsync(staged, ct);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct) => inner.OpenReadAsync(storageKey, ct);
        public Task<bool> DeleteAsync(string storageKey, CancellationToken ct) => inner.DeleteAsync(storageKey, ct);
        public Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct) => inner.DiscardAsync(staged, ct);
    }
}
