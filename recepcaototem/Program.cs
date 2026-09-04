using System.Security.Claims;
using System.Threading.RateLimiting;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Auditing;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Api.Health;
using recepcaototem.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (builder.Environment.IsProduction() && !string.IsNullOrWhiteSpace(connection))
{
    var sql = new SqlConnectionStringBuilder(connection);
    if (!sql.IntegratedSecurity || !string.IsNullOrEmpty(sql.UserID) || !string.IsNullOrEmpty(sql.Password))
        throw new InvalidOperationException("Production requires Windows integrated database authentication.");
}
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connection, sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<IDatabaseProbe, EfDatabaseProbe>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = "__Host-Lumis.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("Administration", p => p.RequireRole(SystemRoles.Administrador));
    options.AddPolicy("Operations", p => p.RequireRole(SystemRoles.Administrador, SystemRoles.Gerente));
    options.AddPolicy("Professional", p => p.RequireRole(SystemRoles.Profissional));
});
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
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
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
app.MapGet("/api/auth/session", (ClaimsPrincipal user) => Results.Ok(new
{
    userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
    roles = user.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray()
})).RequireAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
// No migrations, accounts, role creation, Identity UI or business endpoints during startup.
app.Run();
