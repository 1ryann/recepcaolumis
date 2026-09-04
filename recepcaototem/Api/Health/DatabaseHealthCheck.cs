using GestaoPredio.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace recepcaototem.Api.Health;
public sealed class DatabaseHealthCheck(IDatabaseProbe probe, ILogger<DatabaseHealthCheck> logger) : IHealthCheck {
 public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
  try { return await probe.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); }
  catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return HealthCheckResult.Unhealthy(); }
  catch (Exception) {
   logger.LogWarning("Database readiness probe failed.");
   return HealthCheckResult.Unhealthy();
  }
 }
}
