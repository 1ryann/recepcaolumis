using GestaoPredio.Domain.Rooms;
using recepcaototem.Features.Totem;

namespace GestaoPredio.UnitTests;

public sealed class PublicRoomContractsTests
{
    // This test used to forbid HourlyRate and DailyRate as well: the catalogue deliberately
    // showed no price at all. That decision was reversed — the catalogue now advertises the
    // monthly price with the hourly and daily rates beneath it — so the rates are allowed
    // here on purpose. What remains forbidden is who occupies the room and on what terms.
    [Theory]
    [InlineData(typeof(PublicRoomCard))]
    [InlineData(typeof(PublicRoomDetail))]
    public void Public_contract_excludes_who_occupies_the_room_and_on_what_terms(Type type)
    {
        var names = type.GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Tenant", "TenantId", "Professional", "ProfessionalId",
                     "ContractedRate" })
            Assert.DoesNotContain(forbidden, names);
    }

    // The card is the grid tile: a price, who fits, what kind of room, and nothing that
    // belongs to the detail page.
    [Theory]
    [InlineData(typeof(PublicRoomCard))]
    public void The_card_carries_no_detail_only_field(Type type)
    {
        var names = type.GetProperties().Select(p => p.Name).ToArray();

        foreach (var absent in new[] { "Amenities", "PhotoUrls", "WhatsappUrl", "BathroomCount" })
            Assert.DoesNotContain(absent, names);
    }

    [Fact]
    public void Public_room_card_exposes_availability_cover_photo_price_and_capacity()
    {
        var card = new PublicRoomCard(Guid.NewGuid(), "Sala 1", "Descrição",
            PublicRoomAvailabilityStatus.AvailableSoon, new DateOnly(2026, 11, 16), "/cover.jpg",
            3100m, 4, 8, "CONSULTORIO");

        Assert.Equal("Sala 1", card.Name);
        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, card.Availability);
        Assert.Equal(new DateOnly(2026, 11, 16), card.AvailableFrom);
        Assert.Equal("/cover.jpg", card.CoverPhotoUrl);
        Assert.Equal(3100m, card.MonthlyRate);
        Assert.Equal(4, card.CapacityMin);
        Assert.Equal(8, card.CapacityMax);
        Assert.Equal("CONSULTORIO", card.Category);
    }

    [Fact]
    public void Public_room_detail_exposes_availability_photos_rates_and_features()
    {
        var detail = new PublicRoomDetail(Guid.NewGuid(), "Sala 1", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, ["/one.jpg", "/two.jpg"],
            3100m, 45m, 280m, 25m, 1, 4, 8, "CONSULTORIO", ["CLIMATIZADA"], "https://wa.me/5569999?text=x");

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, detail.Availability);
        Assert.Null(detail.AvailableFrom);
        Assert.Equal(["/one.jpg", "/two.jpg"], detail.PhotoUrls);
        Assert.Equal(3100m, detail.MonthlyRate);
        Assert.Equal(45m, detail.HourlyRate);
        Assert.Equal(25m, detail.AreaSquareMeters);
        Assert.Equal(["CLIMATIZADA"], detail.Amenities);
    }

    // A room nobody has priced or measured yet is an ordinary room, not a broken one.
    [Fact]
    public void Every_catalogue_attribute_may_be_absent()
    {
        var detail = new PublicRoomDetail(Guid.NewGuid(), "Sala crua", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, [],
            null, null, null, null, null, null, null, null, [], null);

        Assert.Null(detail.MonthlyRate);
        Assert.Null(detail.Category);
        Assert.Null(detail.WhatsappUrl);
        Assert.Empty(detail.Amenities);
    }
}
