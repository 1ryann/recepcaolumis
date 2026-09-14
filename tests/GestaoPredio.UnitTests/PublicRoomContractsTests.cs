using GestaoPredio.Domain.Rooms;
using recepcaototem.Features.Totem;

namespace GestaoPredio.UnitTests;

public sealed class PublicRoomContractsTests
{
    [Theory]
    [InlineData(typeof(PublicRoomCard))]
    [InlineData(typeof(PublicRoomDetail))]
    public void Public_contract_excludes_private_and_price_fields(Type type)
    {
        var names = type.GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Tenant", "TenantId", "Professional", "ProfessionalId",
                     "ContractedRate", "HourlyRate", "DailyRate" })
            Assert.DoesNotContain(forbidden, names);
    }

    [Fact]
    public void Public_room_card_exposes_availability_and_cover_photo()
    {
        var card = new PublicRoomCard(Guid.NewGuid(), "Sala 1", "Descrição",
            PublicRoomAvailabilityStatus.AvailableSoon, new DateOnly(2026, 11, 16), "/cover.jpg");

        Assert.Equal("Sala 1", card.Name);
        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, card.Availability);
        Assert.Equal(new DateOnly(2026, 11, 16), card.AvailableFrom);
        Assert.Equal("/cover.jpg", card.CoverPhotoUrl);
    }

    [Fact]
    public void Public_room_detail_exposes_availability_and_photo_urls()
    {
        var detail = new PublicRoomDetail(Guid.NewGuid(), "Sala 1", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, ["/one.jpg", "/two.jpg"]);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, detail.Availability);
        Assert.Null(detail.AvailableFrom);
        Assert.Equal(["/one.jpg", "/two.jpg"], detail.PhotoUrls);
    }
}
