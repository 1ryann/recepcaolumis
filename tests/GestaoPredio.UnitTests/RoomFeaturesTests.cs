using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

// The commercial attributes a room shows on the public catalogue: a monthly price, its
// size, how many people fit, what kind of room it is and which comforts it has. Every one
// of them is optional — a room that has not been measured or priced yet simply shows less,
// and must never be rejected or shown a zero.
public sealed class RoomFeaturesTests
{
    [Fact]
    public void None_is_valid_and_carries_nothing()
    {
        Assert.True(RoomFeatures.None.IsValid());
        Assert.Null(RoomFeatures.None.MonthlyRate);
        Assert.Null(RoomFeatures.None.Category);
        Assert.Empty(RoomFeatures.None.Amenities);
    }

    // xUnit cannot widen an InlineData literal to decimal?, so the cases are spelled out.
    [Fact]
    public void A_monthly_price_follows_the_same_rule_as_the_other_rates()
    {
        foreach (decimal? value in new decimal?[] { null, 0m, 3100m, 3100.55m })
            Assert.True((RoomFeatures.None with { MonthlyRate = value }).IsValid());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3100.555)]
    public void A_monthly_price_that_is_negative_or_needs_rounding_is_rejected(decimal value)
    {
        Assert.False((RoomFeatures.None with { MonthlyRate = value }).IsValid());
    }

    [Theory]
    [InlineData(25)]
    [InlineData(25.5)]
    [InlineData(0.5)]
    public void An_area_is_a_positive_number_of_square_metres(decimal value)
    {
        Assert.True((RoomFeatures.None with { AreaSquareMeters = value }).IsValid());
    }

    [Theory]
    [InlineData(0)]     // a room with no area is not a room
    [InlineData(-25)]
    [InlineData(25.555)]
    [InlineData(100000)]
    public void An_area_that_is_zero_negative_or_absurd_is_rejected(decimal value)
    {
        Assert.False((RoomFeatures.None with { AreaSquareMeters = value }).IsValid());
    }

    [Theory]
    [InlineData(0)]     // a room may genuinely have no bathroom of its own
    [InlineData(1)]
    [InlineData(20)]
    public void A_bathroom_count_may_be_zero(int value)
    {
        Assert.True((RoomFeatures.None with { BathroomCount = value }).IsValid());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void A_negative_or_implausible_bathroom_count_is_rejected(int value)
    {
        Assert.False((RoomFeatures.None with { BathroomCount = value }).IsValid());
    }

    [Fact]
    public void A_capacity_range_needs_a_minimum_and_may_repeat_it_as_the_maximum()
    {
        Assert.True((RoomFeatures.None with { CapacityMin = 4, CapacityMax = 8 }).IsValid());
        Assert.True((RoomFeatures.None with { CapacityMin = 4, CapacityMax = 4 }).IsValid());
        Assert.True((RoomFeatures.None with { CapacityMin = 4 }).IsValid());
    }

    [Fact]
    public void A_capacity_maximum_without_a_minimum_or_below_it_is_rejected()
    {
        Assert.False((RoomFeatures.None with { CapacityMax = 8 }).IsValid());
        Assert.False((RoomFeatures.None with { CapacityMin = 8, CapacityMax = 4 }).IsValid());
        Assert.False((RoomFeatures.None with { CapacityMin = 0 }).IsValid());
        Assert.False((RoomFeatures.None with { CapacityMin = 201 }).IsValid());
    }

    [Fact]
    public void Amenities_must_be_known_and_must_not_repeat()
    {
        Assert.True((RoomFeatures.None with
        {
            Amenities = [RoomAmenity.AirConditioned, RoomAmenity.Furnished],
        }).IsValid());

        Assert.False((RoomFeatures.None with
        {
            Amenities = [RoomAmenity.Furnished, RoomAmenity.Furnished],
        }).IsValid());

        Assert.False((RoomFeatures.None with { Amenities = [(RoomAmenity)99] }).IsValid());
    }

    [Fact]
    public void A_category_outside_the_known_list_is_rejected()
    {
        Assert.True((RoomFeatures.None with { Category = RoomCategory.Consulting }).IsValid());
        Assert.False((RoomFeatures.None with { Category = (RoomCategory)99 }).IsValid());
    }

    // The codes are what reaches the database and the browser, so they are part of the
    // contract: renaming one silently would strip the feature from every stored room.
    [Theory]
    [InlineData(RoomAmenity.AirConditioned, "CLIMATIZADA")]
    [InlineData(RoomAmenity.Furnished, "MOBILIADA")]
    [InlineData(RoomAmenity.Wifi, "WIFI")]
    [InlineData(RoomAmenity.Window, "JANELA")]
    [InlineData(RoomAmenity.Sink, "PIA")]
    [InlineData(RoomAmenity.Accessible, "ACESSIVEL")]
    public void Every_amenity_round_trips_through_its_code(RoomAmenity amenity, string code)
    {
        Assert.Equal(code, RoomAmenityCode.From(amenity));
        Assert.True(RoomAmenityCode.TryParse(code, out var parsed));
        Assert.Equal(amenity, parsed);
    }

    [Theory]
    [InlineData(RoomCategory.Consulting, "CONSULTORIO")]
    [InlineData(RoomCategory.Meeting, "REUNIAO")]
    [InlineData(RoomCategory.Creative, "CRIATIVA")]
    public void Every_category_round_trips_through_its_code(RoomCategory category, string code)
    {
        Assert.Equal(code, RoomCategoryCode.From(category));
        Assert.True(RoomCategoryCode.TryParse(code, out var parsed));
        Assert.Equal(category, parsed);
    }

    // The admin endpoint decides whether an edit changed anything by comparing the stored
    // features with the submitted ones. Record equality would compare the amenity lists by
    // reference, so every save would look like a change.
    [Fact]
    public void Two_features_holding_the_same_amenities_in_different_lists_are_equal()
    {
        var left = RoomFeatures.None with { MonthlyRate = 3100m, Amenities = [RoomAmenity.Wifi, RoomAmenity.Sink] };
        var right = RoomFeatures.None with { MonthlyRate = 3100m, Amenities = [RoomAmenity.Sink, RoomAmenity.Wifi] };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Features_differing_in_one_attribute_are_not_equal()
    {
        var stored = RoomFeatures.None with { MonthlyRate = 3100m, Amenities = [RoomAmenity.Wifi] };

        Assert.NotEqual(stored, stored with { MonthlyRate = 3200m });
        Assert.NotEqual(stored, stored with { Amenities = [RoomAmenity.Wifi, RoomAmenity.Sink] });
        Assert.NotEqual(stored, stored with { Amenities = [RoomAmenity.Sink] });
        Assert.NotEqual(stored, stored with { BathroomCount = 1 });
    }

    [Fact]
    public void An_unknown_code_does_not_parse()
    {
        Assert.False(RoomAmenityCode.TryParse("SAUNA", out _));
        Assert.False(RoomCategoryCode.TryParse("", out _));
    }
}
