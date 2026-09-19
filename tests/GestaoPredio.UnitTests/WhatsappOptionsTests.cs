using Microsoft.Extensions.Options;
using recepcaototem.Features.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class WhatsappOptionsTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Empty_phone_is_allowed_only_in_non_deployed_environments(string environmentName)
    {
        var result = new WhatsappOptionsValidator(environmentName)
            .Validate(null, new WhatsappOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Empty_phone_is_rejected_in_deployed_environments(string environmentName)
    {
        var result = new WhatsappOptionsValidator(environmentName)
            .Validate(null, new WhatsappOptions());

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("(69) 99999-9999", "+5569999999999")]
    [InlineData("+14155552671", "+14155552671")]
    public void Valid_phone_is_normalized_to_e164(string configuredPhone, string expected)
    {
        var options = new WhatsappOptions { FinanceiroPhoneNumber = configuredPhone };

        var result = new WhatsappOptionsValidator("Production").Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, options.FinanceiroPhoneNumber);
    }

    // Development and Testing are both "non-deployed": the integration suite (Testing) covers the endpoint
    // behaviour for Development only because the validator treats the two identically for every input.
    [Theory]
    [InlineData("")]
    [InlineData("55 69 99999-9999")]
    [InlineData("123")]
    [InlineData("(69) 99999-9999")]
    public void Development_and_Testing_validate_every_phone_identically(string configuredPhone)
    {
        var development = new WhatsappOptions { FinanceiroPhoneNumber = configuredPhone };
        var testing = new WhatsappOptions { FinanceiroPhoneNumber = configuredPhone };

        var developmentResult = new WhatsappOptionsValidator("Development").Validate(null, development);
        var testingResult = new WhatsappOptionsValidator("Testing").Validate(null, testing);

        Assert.Equal(testingResult.Succeeded, developmentResult.Succeeded);
        Assert.Equal(testing.FinanceiroPhoneNumber, development.FinanceiroPhoneNumber);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("+44 2071838750")]
    [InlineData("55 69 99999-9999")]
    public void Invalid_phone_is_rejected_in_every_environment(string configuredPhone)
    {
        var result = new WhatsappOptionsValidator("Development")
            .Validate(null, new WhatsappOptions { FinanceiroPhoneNumber = configuredPhone });

        Assert.True(result.Failed);
    }
}
