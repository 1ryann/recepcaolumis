using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class TotemProfessionalsCarouselTests(ModulesApiFactory factory)
{
    private sealed record Card(string Id, string Name, string Profession, string? PhotoUrl, string Status);

    [Fact]
    public async Task Lists_only_active_professionals_ordered_by_name_with_minimal_fields()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var beatriz = Professional.Create("Beatriz Silva", "Nutricionista", "+5569999990001", now);
            var ana = Professional.Create("Ana Souza", "Fisioterapeuta", "+5569999990002", now);
            var inactive = Professional.Create("Zeca Inativo", "Psicólogo", "+5569999990003", now);
            inactive.Deactivate(now);
            db.Professionals.AddRange(beatriz, ana, inactive);
            await db.SaveChangesAsync();
        }

        var response = await factory.Client.GetAsync("/api/totem/professionals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cards = await response.Content.ReadFromJsonAsync<List<Card>>();
        Assert.NotNull(cards);
        Assert.Equal(new[] { "Ana Souza", "Beatriz Silva" }, cards!.Select(c => c.Name).ToArray());
        Assert.All(cards, c => Assert.Null(c.PhotoUrl));
        Assert.All(cards, c => Assert.Equal("UNAVAILABLE", c.Status));

        // Minimization: raw JSON must not carry any extra property.
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var names = el.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "id", "name", "photourl", "profession", "status" }, names);
        }
    }

    [Fact]
    public async Task Status_reflects_in_service_visit_then_effective_presence_then_unavailable()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();   // establishment open "now"
        Guid inServiceId, presentId, absentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var a = Professional.Create("A Atende", "X", "+5569999991001", now);
            var b = Professional.Create("B Presente", "X", "+5569999991002", now);
            var c = Professional.Create("C Ausente", "X", "+5569999991003", now);
            db.Professionals.AddRange(a, b, c);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(a.Id, now));
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(b.Id, now));
            // Adapted: Visits.ReservationId / CustomerId are real FKs, so seed a walk-in visit
            // with null reservation/customer instead of the brief's placeholder GUIDs.
            db.Visits.Add(Visit.Arrive(a.Id, null, null, "Visitante", "TOTEM", now, null));
            await db.SaveChangesAsync();
            var visit = await db.Visits.SingleAsync(v => v.ProfessionalId == a.Id);
            visit.StartService("TOTEM", now);          // -> VisitStatus.InService
            await db.SaveChangesAsync();
            (inServiceId, presentId, absentId) = (a.Id, b.Id, c.Id);
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        string Status(Guid id) => cards!.Single(c => c.Id == id.ToString()).Status;
        Assert.Equal("IN_SERVICE", Status(inServiceId));
        Assert.Equal("AVAILABLE", Status(presentId));
        Assert.Equal("UNAVAILABLE", Status(absentId));
    }

    [Fact]
    public async Task Effective_presence_but_establishment_closed_is_unavailable()
    {
        await factory.ResetAsync();
        // NO operating hours seeded => PresenceEvaluator fails closed for the civil day.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var p = Professional.Create("So Presenca", "X", "+5569999992001", now);
            db.Professionals.Add(p);
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(p.Id, now));
            await db.SaveChangesAsync();
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        Assert.Equal("UNAVAILABLE", Assert.Single(cards!).Status);
    }

    [Fact]
    public async Task Photo_url_is_the_public_totem_path_when_a_photo_exists()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        Guid withPhoto;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var p = Professional.Create("Com Foto", "X", "+5569999993001", now);
            // Adapted: PhotoFileId is a real FK to PrivateFiles, so seed the file row first.
            var photo = PrivateFile.Create(Guid.NewGuid().ToString("N"), "image/png", 10,
                PrivateFilePurposes.ProfessionalPhoto, now);
            p.SetPhoto(photo.Id, now);
            db.PrivateFiles.Add(photo);
            db.Professionals.Add(p);
            await db.SaveChangesAsync();
            withPhoto = p.Id;
        }

        var cards = await factory.Client.GetFromJsonAsync<List<Card>>("/api/totem/professionals");
        Assert.Equal($"/api/totem/professionals/{withPhoto}/photo", Assert.Single(cards!).PhotoUrl);
    }

    [Fact]
    public async Task Public_photo_endpoint_streams_only_for_active_professionals()
    {
        await factory.ResetAsync();
        Guid activeWithPhoto, inactiveWithPhoto, activeNoPhoto;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var a = Professional.Create("Ativo Foto", "X", "+5569999994001", now);
            var b = Professional.Create("Inativo Foto", "X", "+5569999994002", now);
            var c = Professional.Create("Ativo Sem Foto", "X", "+5569999994003", now);
            a.SetPhoto(await SeedPhotoFileAsync(db), now);
            b.SetPhoto(await SeedPhotoFileAsync(db), now);
            b.Deactivate(now);
            db.Professionals.AddRange(a, b, c);
            await db.SaveChangesAsync();
            (activeWithPhoto, inactiveWithPhoto, activeNoPhoto) = (a.Id, b.Id, c.Id);
        }

        var ok = await factory.Client.GetAsync($"/api/totem/professionals/{activeWithPhoto}/photo");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("public, max-age=300", ok.Headers.CacheControl?.ToString());
        Assert.False(string.IsNullOrEmpty(ok.Content.Headers.ContentType?.MediaType));

        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/professionals/{inactiveWithPhoto}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/professionals/{activeNoPhoto}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/professionals/{Guid.NewGuid()}/photo")).StatusCode);
    }

    // Stage a real PROFESSIONAL_PHOTO file through IPrivateFileStorage (mirrors ProfessionalPhotoFailureTests)
    // and return its PrivateFile id. The metadata row is added to the supplied context and persisted by the caller.
    private async Task<Guid> SeedPhotoFileAsync(ApplicationDbContext db)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IPrivateFileStorage>();
        var bytes = TestImageData.Png();
        await using var source = new MemoryStream(bytes);
        var staged = await storage.StageAsync(source, 5 * 1024 * 1024, CancellationToken.None);
        var key = await storage.CommitAsync(staged, CancellationToken.None);
        var file = PrivateFile.Create(key, "image/png", bytes.Length,
            PrivateFilePurposes.ProfessionalPhoto, DateTimeOffset.UtcNow);
        db.PrivateFiles.Add(file);
        return file.Id;
    }
}
