namespace recepcaototem.Api.Middleware;
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger) {
 public async Task InvokeAsync(HttpContext context) {
  try { await next(context); }
  catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
  catch (Exception) {
   logger.LogError("Unhandled request failure. TraceId: {TraceId}", context.TraceIdentifier);
   if (context.Response.HasStarted) { context.Abort(); return; }
   context.Response.Clear();
   context.Response.StatusCode = StatusCodes.Status500InternalServerError;
   await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Não foi possível concluir a operação.", traceId = context.TraceIdentifier });
  }
 }
}
