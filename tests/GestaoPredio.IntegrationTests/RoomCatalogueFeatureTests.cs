using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;

namespace GestaoPredio.IntegrationTests;

// The commercial attributes a room advertises: a monthly price, its size, how many
// bathrooms and people it holds, its category and its comforts. They travel from the admin
// endpoint into the database and out again through the anonymous catalogue, so these tests
// follow one room the whole way rather than asserting each layer on its own.
[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomCatalogueFeatureTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Catalogue_attributes_survive_the_round_trip_to_the_public_detail()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-features-{Guid.NewGuid():N}@lumis.test");
        var name = $"Sala {Guid.NewGuid():N}";

        var created = await CreateAsync(new
        {
            name,
            description = "Espaço de alto padrão.",
            hourlyRate = 45m,
            dailyRate = 280m,
            monthlyRate = 3100m,
            areaSquareMeters = 25.5m,
            bathroomCount = 1,
            capacityMin = 4,
            capacityMax = 8,
            category = "CONSULTORIO",
            amenities = new[] { "CLIMATIZADA", "MOBILIADA" },
        });

        Assert.Equal(3100m, created.MonthlyRate);
        Assert.Equal(25.5m, created.AreaSquareMeters);
        Assert.Equal("CONSULTORIO", created.Category);
        Assert.Equal(["CLIMATIZADA", "MOBILIADA"], created.Amenities);

        var detail = await factory.Client.GetFromJsonAsync<PublicDetail>($"/api/totem/rooms/{created.Id}");
        Assert.NotNull(detail);
        Assert.Equal(3100m, detail!.MonthlyRate);
        Assert.Equal(45m, detail.HourlyRate);
        Assert.Equal(280m, detail.DailyRate);
        Assert.Equal(25.5m, detail.AreaSquareMeters);
        Assert.Equal(1, detail.BathroomCount);
        Assert.Equal(4, detail.CapacityMin);
        Assert.Equal(8, detail.CapacityMax);
        Assert.Equal("CONSULTORIO", detail.Category);
        Assert.Equal(["CLIMATIZADA", "MOBILIADA"], detail.Amenities);

        var cards = await factory.Client.GetFromJsonAsync<PublicCard[]>("/api/totem/rooms");
        var card = Assert.Single(cards!, x => x.Id == created.Id);
        Assert.Equal(3100m, card.MonthlyRate);
        Assert.Equal(4, card.CapacityMin);
        Assert.Equal("CONSULTORIO", card.Category);
    }

    // The state every room starts in: registered, not yet priced, measured or photographed.
    [Fact]
    public async Task A_room_with_no_catalogue_attributes_is_created_and_published_without_them()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-bare-{Guid.NewGuid():N}@lumis.test");

        var created = await CreateAsync(new
        {
            name = $"Sala {Guid.NewGuid():N}", description = (string?)null, hourlyRate = 0m, dailyRate = 0m,
        });

        Assert.Null(created.MonthlyRate);
        Assert.Null(created.Category);
        Assert.Empty(created.Amenities);

        var detail = await factory.Client.GetFromJsonAsync<PublicDetail>($"/api/totem/rooms/{created.Id}");
        Assert.Null(detail!.MonthlyRate);
        Assert.Empty(detail.Amenities);
        // A rate of zero is an unset rate, not an advertisement for a free room.
        Assert.Null(detail.HourlyRate);
        Assert.Null(detail.DailyRate);
    }

    [Theory]
    [InlineData("areaSquareMeters", 0)]
    [InlineData("areaSquareMeters", -5)]
    [InlineData("bathroomCount", -1)]
    [InlineData("bathroomCount", 21)]
    [InlineData("capacityMin", 0)]
    [InlineData("monthlyRate", -1)]
    public async Task An_out_of_range_attribute_is_refused_with_its_own_error_code(string field, decimal value)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-bad-{Guid.NewGuid():N}@lumis.test");

        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Sala {Guid.NewGuid():N}",
            ["description"] = null,
            ["hourlyRate"] = 10m,
            ["dailyRate"] = 50m,
            [field] = value,
        };

        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("INVALID_ROOM_FEATURES", error!.Code);
    }

    [Fact]
    public async Task A_maximum_capacity_below_its_minimum_or_without_one_is_refused()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-cap-{Guid.NewGuid():N}@lumis.test");

        foreach (var capacity in new[] { (min: (int?)null, max: (int?)8), (min: 8, max: 4) })
        {
            var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
            {
                name = $"Sala {Guid.NewGuid():N}", description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
                capacityMin = capacity.min, capacityMax = capacity.max,
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("SAUNA")]
    [InlineData("consultorio")]
    public async Task An_unknown_category_code_is_refused_rather_than_stored_as_written(string category)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-cat-{Guid.NewGuid():N}@lumis.test");

        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
        {
            name = $"Sala {Guid.NewGuid():N}", description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
            category,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_amenity_code_is_refused()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-amen-{Guid.NewGuid():N}@lumis.test");

        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
        {
            name = $"Sala {Guid.NewGuid():N}", description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
            amenities = new[] { "CLIMATIZADA", "SAUNA" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The update endpoint returns early when nothing changed. Before the features joined
    // that comparison, an edit that touched only them was accepted and silently discarded.
    [Fact]
    public async Task An_edit_that_changes_only_the_catalogue_attributes_is_saved()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-edit-{Guid.NewGuid():N}@lumis.test");
        var name = $"Sala {Guid.NewGuid():N}";
        var created = await CreateAsync(new
        {
            name, description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
        });

        var updated = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", new
        {
            name, description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
            concurrencyToken = created.ConcurrencyToken,
            monthlyRate = 3100m, category = "REUNIAO", amenities = new[] { "WIFI" },
        });
        updated.EnsureSuccessStatusCode();

        var detail = await factory.Client.GetFromJsonAsync<PublicDetail>($"/api/totem/rooms/{created.Id}");
        Assert.Equal(3100m, detail!.MonthlyRate);
        Assert.Equal("REUNIAO", detail.Category);
        Assert.Equal(["WIFI"], detail.Amenities);
    }

    // Omitting them on an update clears them, the same way omitting a description does —
    // Update replaces the room's details wholesale.
    [Fact]
    public async Task An_update_that_omits_the_attributes_clears_them()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-clear-{Guid.NewGuid():N}@lumis.test");
        var name = $"Sala {Guid.NewGuid():N}";
        var created = await CreateAsync(new
        {
            name, description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
            monthlyRate = 3100m, amenities = new[] { "WIFI" },
        });

        var updated = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", new
        {
            name, description = (string?)null, hourlyRate = 10m, dailyRate = 50m,
            concurrencyToken = created.ConcurrencyToken,
        });
        updated.EnsureSuccessStatusCode();

        var detail = await factory.Client.GetFromJsonAsync<PublicDetail>($"/api/totem/rooms/{created.Id}");
        Assert.Null(detail!.MonthlyRate);
        Assert.Empty(detail.Amenities);
    }

    // The catalogue advertises prices now, but never who is in the room or on what terms.
    [Fact]
    public async Task The_public_payload_still_carries_nothing_about_the_occupant()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-priv-{Guid.NewGuid():N}@lumis.test");
        var created = await CreateAsync(new
        {
            name = $"Sala {Guid.NewGuid():N}", description = (string?)null,
            hourlyRate = 10m, dailyRate = 50m, monthlyRate = 3100m,
        });

        var raw = await factory.Client.GetStringAsync($"/api/totem/rooms/{created.Id}");
        foreach (var forbidden in new[] { "tenant", "professional", "contractedRate" })
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RoomPayload> CreateAsync(object body)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomPayload>())!;
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record RoomPayload(Guid Id, string ConcurrencyToken, decimal? MonthlyRate,
        decimal? AreaSquareMeters, int? BathroomCount, int? CapacityMin, int? CapacityMax,
        string? Category, IReadOnlyList<string> Amenities);

    private sealed record PublicCard(Guid Id, decimal? MonthlyRate, int? CapacityMin, int? CapacityMax,
        string? Category);

    private sealed record PublicDetail(Guid Id, decimal? MonthlyRate, decimal? HourlyRate, decimal? DailyRate,
        decimal? AreaSquareMeters, int? BathroomCount, int? CapacityMin, int? CapacityMax,
        string? Category, IReadOnlyList<string> Amenities, string? WhatsappUrl);

    private sealed record ErrorPayload(string Code, string Message);
}
