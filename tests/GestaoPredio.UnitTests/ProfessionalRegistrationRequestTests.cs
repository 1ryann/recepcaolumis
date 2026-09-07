using GestaoPredio.Domain.ProfessionalRegistrations;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalRegistrationRequestTests
{
    [Fact]
    public void Creates_pending_and_allows_one_review()
    {
        var now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
        var request = ProfessionalRegistrationRequest.Create("user-1", "  Ana   Lima ", " Fisioterapia ",
            "(69) 99999-9999", "  Atendimento humanizado. ", now);

        Assert.Equal(ProfessionalRegistrationStatus.Pending, request.Status);
        Assert.Equal("Ana Lima", request.Name);
        Assert.Equal("+5569999999999", request.WhatsApp);
        request.Approve("reviewer", now);
        Assert.Equal(ProfessionalRegistrationStatus.Approved, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.Approve("reviewer", now));
    }

    [Theory]
    [InlineData("<b>texto</b>")]
    [InlineData("<script>")]
    public void Rejects_html_description(string description) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfessionalRegistrationRequest.Create(
            "user", "Ana", "Fisio", "69999999999", description, DateTimeOffset.UtcNow));
}
