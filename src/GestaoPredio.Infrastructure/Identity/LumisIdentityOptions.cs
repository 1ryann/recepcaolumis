using Microsoft.AspNetCore.Identity;

namespace GestaoPredio.Infrastructure.Identity;

public static class LumisIdentityOptions
{
    public static void Configure(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        // Operator's decision: a short password people can actually remember. Letter + digit only.
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    }
}
