using GestaoPredio.Application.Dashboard;

namespace recepcaototem.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/dashboard", Read)
            .RequireAuthorization("Operations");
        return endpoints;
    }

    private static async Task<IResult> Read(
        IDashboardReader dashboard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await dashboard.ReadAsync(clock.GetUtcNow(), cancellationToken));
    }
}
