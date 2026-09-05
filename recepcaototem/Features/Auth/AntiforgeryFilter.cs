using Microsoft.AspNetCore.Antiforgery;

namespace recepcaototem.Features.Auth;

public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { code = "INVALID_CSRF", message = "Não foi possível validar a solicitação." });
        }
        return await next(context);
    }
}
