using System.Security.Claims;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace recepcaototem.Api.Configuration;

public static class IdentityConfiguration
{
    public const string ActiveUserPolicy = "ActiveUser";
    public const string PasswordChangedPolicy = "PasswordChanged";
    public const string CustomerPolicy = "Customer";
    public const string ActiveClaim = "lumis:active";
    public const string MustChangePasswordClaim = "lumis:must_change_password";

    public static IServiceCollection AddLumisIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(LumisIdentityOptions.Configure)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, LumisUserClaimsPrincipalFactory>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = "__Host-Lumis.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));
        return services;
    }

    public static void AddLumisAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build();
            options.AddPolicy(ActiveUserPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(ActiveClaim, "true"));
            options.AddPolicy(PasswordChangedPolicy, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(ActiveClaim, "true").RequireClaim(MustChangePasswordClaim, "false"));
            options.AddPolicy("Administration", policy => policy.RequireRole(SystemRoles.Administrador)
                .RequireClaim(ActiveClaim, "true").RequireClaim(MustChangePasswordClaim, "false"));
            options.AddPolicy("Operations", policy => policy.RequireRole(SystemRoles.Administrador, SystemRoles.Gerente)
                .RequireClaim(ActiveClaim, "true").RequireClaim(MustChangePasswordClaim, "false"));
            options.AddPolicy("Professional", policy => policy.RequireRole(SystemRoles.Profissional)
                .RequireClaim(ActiveClaim, "true").RequireClaim(MustChangePasswordClaim, "false"));
            options.AddPolicy(CustomerPolicy, policy => policy.RequireRole(SystemRoles.Customer)
                .RequireClaim(ActiveClaim, "true").RequireClaim(MustChangePasswordClaim, "false"));
        });
    }
}

public sealed class LumisUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(IdentityConfiguration.ActiveClaim, user.IsActive ? "true" : "false"));
        identity.AddClaim(new Claim(IdentityConfiguration.MustChangePasswordClaim, user.MustChangePassword ? "true" : "false"));
        identity.AddClaim(new Claim("lumis:display_name", user.DisplayName));
        return identity;
    }
}
