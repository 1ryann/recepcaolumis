using System.Threading.RateLimiting;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Auditing;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Api.Health;
using recepcaototem.Api.Middleware;
using recepcaototem.Api.Configuration;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Users;
using recepcaototem.Features.Common;
using recepcaototem.Features.Professionals;
using recepcaototem.Features.Rooms;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(StrictBody.Configure);
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connection, postgres => postgres.CommandTimeout(5)));
builder.Services.AddScoped<IDatabaseProbe, EfDatabaseProbe>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddLumisIdentity();
builder.Services.AddLumisAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddScoped<AuthAuditService>();
builder.Services.AddPrivateFileStorage(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Lumis.Csrf";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
var keys = builder.Services.AddDataProtection().SetApplicationName("LumisApi");
var keyPath = builder.Configuration["Security:DataProtectionPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    keys.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (OperatingSystem.IsWindows()) keys.ProtectKeysWithDpapi();
}
else if (builder.Environment.IsProduction())
    throw new InvalidOperationException("Security:DataProtectionPath must be configured externally in production.");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0)
    {
        if (origins.Any(x => x.Contains('*') || !Uri.TryCreate(x, UriKind.Absolute, out _)))
            throw new InvalidOperationException("CORS requires explicit valid origins.");
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));
var permits = builder.Configuration.GetValue("RateLimiting:PermitLimit", 120);
var window = builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60);
if (permits < 1 || window < 1) throw new InvalidOperationException("Invalid rate limiting configuration.");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Separate readiness/application budgets by actual peer; do not trust arbitrary forwarded headers.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter($"{(context.Request.Path == "/health/ready" ? "ready" : "api")}:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits, Window = TimeSpan.FromSeconds(window), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(6));
if (builder.Environment.IsDevelopment()) builder.Services.AddOpenApi();
var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; media-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    // Only infrastructure probes support the requested internal plain HTTP binding.
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps &&
        context.Request.Path != "/health" && context.Request.Path != "/health/ready")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code = "HTTPS_REQUIRED" });
        return;
    }
    await next(context);
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
static Task WriteHealth(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report) =>
    context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealth }).AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = WriteHealth }).AllowAnonymous();
app.MapAuthEndpoints();
app.MapUserAdministrationEndpoints();
app.MapProfessionalEndpoints();
app.MapProfessionalUserLinkEndpoints();
app.MapProfessionalPhotoEndpoints();
app.MapRoomEndpoints();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.Map("/api/{**path}", () => Results.NotFound()).RequireAuthorization();
app.MapFallbackToFile("index.html").AllowAnonymous();
// No migrations, accounts, role creation, Identity UI or business endpoints during startup.
app.Run();
