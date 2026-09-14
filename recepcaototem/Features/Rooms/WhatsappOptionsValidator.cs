using GestaoPredio.Domain.Professionals;
using Microsoft.Extensions.Options;

namespace recepcaototem.Features.Rooms;

public sealed class WhatsappOptionsValidator(string environmentName) : IValidateOptions<WhatsappOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsappOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FinanceiroPhoneNumber))
        {
            return IsNonDeployedEnvironment()
                ? ValidateOptionsResult.Success
                : Failure();
        }

        if (!WhatsAppNormalizer.TryNormalize(options.FinanceiroPhoneNumber, out var normalized))
            return Failure();

        options.FinanceiroPhoneNumber = normalized;
        return ValidateOptionsResult.Success;
    }

    private bool IsNonDeployedEnvironment() =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    private static ValidateOptionsResult Failure() =>
        ValidateOptionsResult.Fail("A configuração do WhatsApp financeiro é obrigatória e deve conter um número válido.");
}
