using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace GestaoPredio.AdminCli;

public enum BootstrapOutcome { Created, AlreadyProvisioned, InvalidInput, Failed }

public sealed class AdminBootstrapper(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    TimeProvider timeProvider)
{
    public async Task<BootstrapOutcome> BootstrapAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        displayName = displayName.Trim();
        email = email.Trim();
        if (displayName.Length is < 1 or > 200 || email.Length is < 3 or > 256)
            return BootstrapOutcome.InvalidInput;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var role in SystemRoles.AuthenticationRoles)
        {
            if (await roles.RoleExistsAsync(role)) continue;
            if (!(await roles.CreateAsync(new IdentityRole(role))).Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BootstrapOutcome.Failed;
            }
        }

        if ((await users.GetUsersInRoleAsync(SystemRoles.Administrador)).Count > 0)
        {
            // Role provisioning is independent from creating the first admin.
            // Commit any missing roles even when the administrator already exists;
            // otherwise a newly introduced role (such as CUSTOMER) is silently
            // discarded and its public registration flow remains unavailable.
            await transaction.CommitAsync(cancellationToken);
            return BootstrapOutcome.AlreadyProvisioned;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsActive = true,
            MustChangePassword = false
        };
        if (!(await users.CreateAsync(user, password)).Succeeded ||
            !(await users.AddToRoleAsync(user, SystemRoles.Administrador)).Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BootstrapOutcome.InvalidInput;
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            TargetUserId = user.Id,
            IpAddress = null,
            Action = "BOOTSTRAP_ADMIN_CREATED",
            Result = "SUCCEEDED",
            OccurredAt = timeProvider.GetUtcNow(),
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BootstrapOutcome.Created;
    }
}
