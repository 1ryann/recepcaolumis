using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class PublicRoomCatalogTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Anonymous_catalog_calculates_availability_orders_results_and_hides_private_fields()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var available = Room.Create("Sala Alfa", "Livre", 100m, 500m, factory.UtcNow);
        var soon = Room.Create("Sala Beta", "Em breve", 100m, 500m, factory.UtcNow);
        var scheduled = Room.Create("Sala Gama", null, 100m, 500m, factory.UtcNow);
        var occupied = Room.Create("Sala Ocupada", null, 100m, 500m, factory.UtcNow);
        var inactive = Room.Create("Sala Inativa", null, 100m, 500m, factory.UtcNow);
        var ending = Room.Create("Sala Encerrando", null, 100m, 500m, factory.UtcNow);
        var cancelled = Room.Create("Sala Cancelada", null, 100m, 500m, factory.UtcNow);
        var ended = Room.Create("Sala Finalizada", null, 100m, 500m, factory.UtcNow);
        inactive.Deactivate(factory.UtcNow);
        await SeedAsync([available, soon, scheduled, occupied, inactive, ending, cancelled, ended], (tenantId, professionalId) =>
        {
            var cancelledLease = LeaseFor(cancelled, tenantId, professionalId, factory.UtcNow.AddDays(1), factory.UtcNow.AddDays(3));
            cancelledLease.Cancel(factory.UtcNow);
            var endedLease = LeaseFor(ended, tenantId, professionalId, factory.UtcNow.AddDays(-3), factory.UtcNow.AddDays(-1));
            endedLease.MarkEndingPending(factory.UtcNow.AddDays(-1));
            endedLease.MarkEnded(factory.UtcNow);
            return [
                LeaseFor(soon, tenantId, professionalId, factory.UtcNow.AddDays(-1), factory.UtcNow.AddDays(1)),
                LeaseFor(scheduled, tenantId, professionalId, factory.UtcNow.AddDays(1), factory.UtcNow.AddDays(3)),
                LeaseFor(occupied, tenantId, professionalId, factory.UtcNow.AddDays(-1), null),
                LeaseFor(ending, tenantId, professionalId, factory.UtcNow.AddDays(-3), factory.UtcNow.AddDays(-1)),
                cancelledLease,
                endedLease
            ];
        });

        var response = await factory.Client.GetAsync("/api/totem/rooms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rooms = (await response.Content.ReadFromJsonAsync<PublicRoomCardPayload[]>())!;
        Assert.Equal(["Sala Alfa", "Sala Beta", "Sala Cancelada", "Sala Encerrando", "Sala Finalizada", "Sala Gama"], rooms.Select(x => x.Name));
        Assert.Equal("AVAILABLE_NOW", rooms.Single(x => x.Id == available.Id).Availability);
        Assert.Null(rooms.Single(x => x.Id == available.Id).AvailableFrom);
        Assert.Equal("AVAILABLE_SOON", rooms.Single(x => x.Id == soon.Id).Availability);
        Assert.Equal("2026-11-16", rooms.Single(x => x.Id == soon.Id).AvailableFrom);
        Assert.Equal("2026-11-18", rooms.Single(x => x.Id == scheduled.Id).AvailableFrom);
        var json = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "tenant", "professional", "contractedRate", "hourlyRate", "dailyRate", "storageKey", "privateFileId" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_orders_cover_then_gallery_and_public_photo_streams_with_public_cache()
    {
        await factory.ResetAsync();
        factory.FreezeTime(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
        var room = Room.Create("Sala Galeria", null, 100m, 500m, factory.UtcNow);
        await SeedAsync([room]);
        var second = await SeedPhotoAsync(room.Id, sortOrder: 1, isCover: false);
        var cover = await SeedPhotoAsync(room.Id, sortOrder: 3, isCover: true);
        var first = await SeedPhotoAsync(room.Id, sortOrder: 0, isCover: false);

        var response = await factory.Client.GetAsync($"/api/totem/rooms/{room.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<PublicRoomDetailPayload>())!;
        Assert.Equal(new[]
        {
            $"/api/totem/rooms/{room.Id}/photos/{cover.PhotoId}?v={cover.FileId}",
            $"/api/totem/rooms/{room.Id}/photos/{first.PhotoId}?v={first.FileId}",
            $"/api/totem/rooms/{room.Id}/photos/{second.PhotoId}?v={second.FileId}"
        }, detail.PhotoUrls);
        var list = (await factory.Client.GetFromJsonAsync<PublicRoomCardPayload[]>("/api/totem/rooms"))!;
        Assert.Equal(detail.PhotoUrls[0], Assert.Single(list).CoverPhotoUrl);
        var photo = await factory.Client.GetAsync(detail.PhotoUrls[0]);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=3600", photo.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Assert.Single(photo.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Inactive_or_openly_occupied_rooms_and_their_photos_are_not_publicly_discoverable()
    {
        await factory.ResetAsync();
        var inactive = Room.Create("Sala Inativa", null, 1m, 1m, factory.UtcNow);
        inactive.Deactivate(factory.UtcNow);
        var occupied = Room.Create("Sala Ocupada", null, 1m, 1m, factory.UtcNow);
        await SeedAsync([inactive, occupied], (tenantId, professionalId) =>
            [LeaseFor(occupied, tenantId, professionalId, factory.UtcNow.AddDays(-1), null)]);
        var photo = await SeedPhotoAsync(inactive.Id, 0, true);

        foreach (var room in new[] { inactive, occupied })
            Assert.Equal(HttpStatusCode.NotFound, (await factory.Client.GetAsync($"/api/totem/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/rooms/{inactive.Id}/photos/{photo.PhotoId}")).StatusCode);
    }

    [Fact]
    public async Task Public_catalog_returns_stable_rate_limit_error_when_customer_budget_is_exhausted()
    {
        await factory.ResetAsync();
        await SeedAsync([Room.Create("Sala Limitada", null, 1m, 1m, factory.UtcNow)]);
        using var throttled = factory.WithConfig(
            ("RateLimiting:CustomerIpPermitLimit", "1"),
            ("RateLimiting:CustomerIdentifierPermitLimit", "1"));

        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync("/api/totem/rooms")).StatusCode);
        var response = await throttled.Client.GetAsync("/api/totem/rooms");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Contains("TOO_MANY_REQUESTS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Public_room_photo_returns_stable_rate_limit_error_when_its_own_budget_is_exhausted_leaving_catalog_untouched()
    {
        await factory.ResetAsync();
        var room = Room.Create("Sala Fotos Limitadas", null, 1m, 1m, factory.UtcNow);
        await SeedAsync([room]);
        var photo = await SeedPhotoAsync(room.Id, 0, true);
        using var throttled = factory.WithConfig(
            ("RateLimiting:RoomPhotoIpPermitLimit", "1"),
            ("RateLimiting:RoomPhotoIdentifierPermitLimit", "10000"),
            ("RateLimiting:CustomerIpPermitLimit", "10000"),
            ("RateLimiting:CustomerIdentifierPermitLimit", "10000"));
        var photoUrl = $"/api/totem/rooms/{room.Id}/photos/{photo.PhotoId}";

        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync(photoUrl)).StatusCode);
        var exhausted = await throttled.Client.GetAsync(photoUrl);
        Assert.Equal((HttpStatusCode)429, exhausted.StatusCode);
        Assert.Contains("TOO_MANY_REQUESTS", await exhausted.Content.ReadAsStringAsync());

        // The photo route's own exhausted budget must not touch the catalog list/detail budget.
        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync("/api/totem/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync($"/api/totem/rooms/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task Exhausting_the_catalog_budget_does_not_affect_the_room_photo_budget()
    {
        await factory.ResetAsync();
        var room = Room.Create("Sala Catalogo Limitado", null, 1m, 1m, factory.UtcNow);
        await SeedAsync([room]);
        var photo = await SeedPhotoAsync(room.Id, 0, true);
        using var throttled = factory.WithConfig(
            ("RateLimiting:CustomerIpPermitLimit", "1"),
            ("RateLimiting:CustomerIdentifierPermitLimit", "10000"),
            ("RateLimiting:RoomPhotoIpPermitLimit", "10000"),
            ("RateLimiting:RoomPhotoIdentifierPermitLimit", "10000"));

        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync("/api/totem/rooms")).StatusCode);
        var exhausted = await throttled.Client.GetAsync("/api/totem/rooms");
        Assert.Equal((HttpStatusCode)429, exhausted.StatusCode);
        Assert.Contains("TOO_MANY_REQUESTS", await exhausted.Content.ReadAsStringAsync());

        // The catalog's own exhausted budget must not touch the room photo budget.
        var photoUrl = $"/api/totem/rooms/{room.Id}/photos/{photo.PhotoId}";
        Assert.Equal(HttpStatusCode.OK, (await throttled.Client.GetAsync(photoUrl)).StatusCode);
    }

    private async Task SeedAsync(IReadOnlyList<Room> rooms,
        Func<Guid, Guid, IReadOnlyList<Lease>>? createLeases = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Rooms.AddRange(rooms);
        if (createLeases is not null)
        {
            var tenant = Tenant.Create("Locatário Catálogo", TenantKind.Individual, factory.UtcNow);
            var professional = Professional.Create("Profissional Catálogo", "Teste", "65999990003", factory.UtcNow);
            db.AddRange(tenant, professional);
            var leases = createLeases(tenant.Id, professional.Id);
            db.Leases.AddRange(leases);
        }
        await db.SaveChangesAsync();
    }

    private Lease LeaseFor(Room room, Guid tenantId, Guid professionalId, DateTimeOffset start, DateTimeOffset? end) => Lease.Create(
        tenantId, professionalId, room.Id, LeaseMode.Monthly, 100m,
        start.AddDays(-1), 10, start, end, 10, factory.UtcNow);

    private async Task<(Guid PhotoId, Guid FileId)> SeedPhotoAsync(Guid roomId, int sortOrder, bool isCover)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var storage = services.GetRequiredService<IPrivateFileStorage>();
        await using var source = new MemoryStream(TestImageData.Png());
        var staged = await storage.StageAsync(source, 5 * 1024 * 1024, default);
        var key = await storage.CommitAsync(staged, default);
        var file = PrivateFile.Create(key, "image/png", TestImageData.Png().Length, PrivateFilePurposes.RoomPhoto, factory.UtcNow);
        var photo = RoomPhoto.Attach(roomId, file.Id, sortOrder, isCover, factory.UtcNow);
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.AddRange(file, photo);
        await db.SaveChangesAsync();
        return (photo.Id, file.Id);
    }

    private sealed record PublicRoomCardPayload(Guid Id, string Name, string Availability, string? AvailableFrom, string? CoverPhotoUrl);
    private sealed record PublicRoomDetailPayload(Guid Id, string Name, string Availability, string? AvailableFrom, string[] PhotoUrls);
}
