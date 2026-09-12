using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalPhotoTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Photo_routes_require_operations_and_mutations_require_antiforgery()
    {
        await factory.ResetAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.GetAsync($"/api/admin/professionals/{id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PutPhotoAsync(factory.Client, id, "x", TestImageData.Png(), "photo.png", "image/png", csrf: null)).StatusCode);

        await LoginAsAsync(SystemRoles.Profissional, "photo-prof@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync($"/api/admin/professionals/{id}/photo")).StatusCode);

        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "photo-csrf@lumis.test");
        var professional = await CreateProfessionalAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PutPhotoAsync(factory.Client, professional.Id, professional.ConcurrencyToken,
                TestImageData.Png(), "photo.png", "image/png", csrf: null)).StatusCode);
    }

    [Fact]
    public async Task Upload_and_get_return_validated_bytes_and_safe_headers()
    {
        await PrepareAdminAsync("photo-upload@lumis.test");
        var professional = await CreateProfessionalAsync();
        var bytes = TestImageData.Png();
        var upload = await PutPhotoWithCsrfAsync(professional, bytes, "dra.ana.png", "IMAGE/PNG");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var updated = (await upload.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.True(updated.HasPhoto);
        Assert.NotEqual(professional.ConcurrencyToken, updated.ConcurrencyToken);

        var get = await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", get.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("private", get.Headers.CacheControl?.ToString());
        Assert.Contains("no-store", get.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metadata = await db.PrivateFiles.SingleAsync();
        Assert.Equal("image/png", metadata.MimeType);
        Assert.Equal(bytes.Length, metadata.Length);
        var audit = await db.AuditEntries.SingleAsync(x => x.Action == "PROFESSIONAL_PHOTO_UPLOADED");
        var serialized = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("dra.ana", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(metadata.StorageKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("image/png", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("jpeg", "photo.jpg", "image/jpeg")]
    [InlineData("jpeg", "photo.JPEG", "IMAGE/JPEG")]
    [InlineData("png", "photo.png", "image/png")]
    [InlineData("webp", "photo.webp", "image/webp")]
    public async Task All_approved_formats_are_accepted_by_the_endpoint(string format, string filename, string mime)
    {
        await PrepareAdminAsync($"photo-{format}-{Guid.NewGuid():N}@lumis.test");
        var professional = await CreateProfessionalAsync();
        var bytes = format switch
        {
            "jpeg" => TestImageData.Jpeg(),
            "png" => TestImageData.Png(),
            "webp" => TestImageData.WebP(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        var response = await PutPhotoWithCsrfAsync(professional, bytes, filename, mime);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var get = await factory.Client.GetAsync($"/api/admin/professionals/{professional.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(mime.ToLowerInvariant(), get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Oversized_upload_is_rejected_without_artifacts()
    {
        await PrepareAdminAsync("photo-large@lumis.test");
        var professional = await CreateProfessionalAsync();
        var bytes = new byte[(5 * 1024 * 1024) + 1];
        TestImageData.Png().CopyTo(bytes, 0);

        var response = await PutPhotoWithCsrfAsync(professional, bytes, "large.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PROFESSIONAL_PHOTO", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, ".staging")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
    }

    [Fact]
    public async Task Replace_then_remove_cleans_old_bytes_and_metadata_after_each_commit()
    {
        await PrepareAdminAsync("photo-lifecycle@lumis.test");
        var professional = await CreateProfessionalAsync();
        var first = (await (await PutPhotoWithCsrfAsync(professional, TestImageData.Png(), "one.png", "image/png"))
            .Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        var second = (await (await PutPhotoWithCsrfAsync(first, TestImageData.Png(), "two.png", "image/png"))
            .Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.NotEqual(first.ConcurrencyToken, second.ConcurrencyToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.PrivateFiles.CountAsync());
        }
        Assert.Single(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));

        var remove = await factory.DeleteWithCsrfAsync($"/api/admin/professionals/{professional.Id}/photo",
            new { concurrencyToken = second.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var removed = (await remove.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        Assert.False(removed.HasPhoto);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await verifyDb.PrivateFiles.CountAsync());
        Assert.Equal(1, await verifyDb.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_PHOTO_REPLACED"));
        Assert.Equal(1, await verifyDb.AuditEntries.CountAsync(x => x.Action == "PROFESSIONAL_PHOTO_REMOVED"));
    }

    [Theory]
    [InlineData("photo.exe", "image/png", true)]
    [InlineData("photo.png", "image/jpeg", true)]
    [InlineData("photo.png", "image/png", false)]
    [InlineData("photo.jpg", "image/jpeg", true)]
    public async Task Invalid_photo_variants_leave_no_file_metadata_or_success_audit(string filename, string mime, bool validBytes)
    {
        await PrepareAdminAsync($"photo-invalid-{Guid.NewGuid():N}@lumis.test");
        var professional = await CreateProfessionalAsync();
        var bytes = validBytes ? TestImageData.Png() : new byte[] { 137, 80, 78 };
        var response = await PutPhotoWithCsrfAsync(professional, bytes, filename, mime);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PROFESSIONAL_PHOTO", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, ".staging")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.PrivateFiles.CountAsync());
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action.StartsWith("PROFESSIONAL_PHOTO_")));
    }

    [Fact]
    public async Task Missing_invalid_and_stale_tokens_are_rejected_before_mutation()
    {
        await PrepareAdminAsync("photo-token@lumis.test");
        var professional = await CreateProfessionalAsync();
        foreach (var token in new string?[] { null, "invalid", "AQID" })
        {
            var response = await PutPhotoWithCsrfAsync(professional with { ConcurrencyToken = token! },
                TestImageData.Png(), "photo.png", "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var first = (await (await PutPhotoWithCsrfAsync(professional, TestImageData.Png(), "first.png", "image/png"))
            .Content.ReadFromJsonAsync<ProfessionalPayload>())!;
        var stale = await PutPhotoWithCsrfAsync(professional, TestImageData.Png(), "stale.png", "image/png");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.True(first.HasPhoto);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(factory.PrivateFilesRoot, "files")));
    }

    [Fact]
    public async Task Stale_token_on_delete_returns_conflict_and_leaves_photo_intact()
    {
        await PrepareAdminAsync("photo-delete-race@lumis.test");
        var professional = await CreateProfessionalAsync();
        var uploaded = (await (await PutPhotoWithCsrfAsync(professional, TestImageData.Png(), "photo.png", "image/png"))
            .Content.ReadFromJsonAsync<ProfessionalPayload>())!;

        var bumped = await factory.PutWithCsrfAsync($"/api/admin/professionals/{uploaded.Id}", new
        {
            name = "Ana Renamed", profession = "Fisio", whatsApp = "65999999999",
            concurrencyToken = uploaded.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, bumped.StatusCode);

        var stale = await factory.DeleteWithCsrfAsync($"/api/admin/professionals/{uploaded.Id}/photo",
            new { concurrencyToken = uploaded.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull((await db.Professionals.AsNoTracking().SingleAsync()).PhotoFileId);
        Assert.Equal(1, await db.PrivateFiles.CountAsync());
    }

    private async Task PrepareAdminAsync(string email)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, email);
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private async Task<ProfessionalPayload> CreateProfessionalAsync()
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/professionals", new
            { name = "Ana", profession = "Fisio", whatsApp = "65999999999" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfessionalPayload>())!;
    }

    private async Task<HttpResponseMessage> PutPhotoWithCsrfAsync(ProfessionalPayload professional,
        byte[] bytes, string filename, string mime) =>
        await PutPhotoAsync(factory.Client, professional.Id, professional.ConcurrencyToken, bytes, filename, mime,
            await factory.GetCsrfTokenAsync());

    internal static async Task<HttpResponseMessage> PutPhotoAsync(HttpClient client, Guid id, string token,
        byte[] bytes, string filename, string mime, string? csrf)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token ?? ""), "concurrencyToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
        multipart.Add(file, "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/professionals/{id}/photo")
            { Content = multipart };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    internal sealed record ProfessionalPayload(Guid Id, bool HasPhoto, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}
