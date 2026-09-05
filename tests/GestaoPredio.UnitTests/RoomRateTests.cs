using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class RoomRateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(150.5)]
    [InlineData(150.55)]
    public void IsValid_accepts_non_negative_values_with_up_to_two_fractional_digits(decimal value)
    {
        Assert.True(RoomRate.IsValid(value));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(150.555)]
    public void IsValid_rejects_negative_values_or_values_that_would_require_rounding(decimal value)
    {
        Assert.False(RoomRate.IsValid(value));
    }

    [Fact]
    public void IsValid_accepts_the_application_maximum_without_transforming_it()
    {
        const decimal maximum = 9_999_999_999_999.99m;

        Assert.True(RoomRate.IsValid(maximum));
    }

    [Fact]
    public void IsValid_rejects_one_cent_above_the_application_maximum()
    {
        const decimal aboveMaximum = 10_000_000_000_000.00m;

        Assert.False(RoomRate.IsValid(aboveMaximum));
    }
}
