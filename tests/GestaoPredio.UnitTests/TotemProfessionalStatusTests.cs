using recepcaototem.Features.Totem;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class TotemProfessionalStatusTests
{
    [Theory]
    [InlineData(true, true, "IN_SERVICE")]
    [InlineData(true, false, "IN_SERVICE")]
    [InlineData(false, true, "AVAILABLE")]
    [InlineData(false, false, "UNAVAILABLE")]
    public void Resolve_maps_presence_and_service_to_the_three_totem_states(
        bool inService, bool effectivePresence, string expected)
    {
        Assert.Equal(expected, TotemProfessionalStatus.Resolve(inService, effectivePresence));
    }
}
