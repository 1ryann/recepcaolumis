using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace recepcaototem.Features.Professionals;

public static class EligibleUserQuery
{
    public static IQueryable<ApplicationUser> Create(ApplicationDbContext db) =>
        WithRequiredRoleProfile(db).Where(user =>
            !db.Professionals.Any(professional => professional.ApplicationUserId == user.Id));

    public static IQueryable<ApplicationUser> WithRequiredRoleProfile(ApplicationDbContext db)
    {
        var professionalUsers = UsersInRole(db, SystemRoles.Profissional);
        var administratorUsers = UsersInRole(db, SystemRoles.Administrador);
        var managerUsers = UsersInRole(db, SystemRoles.Gerente);
        return db.Users.Where(user => user.IsActive &&
            professionalUsers.Contains(user.Id) &&
            !administratorUsers.Contains(user.Id) &&
            !managerUsers.Contains(user.Id));
    }

    private static IQueryable<string> UsersInRole(ApplicationDbContext db, string role) =>
        from userRole in db.UserRoles
        join identityRole in db.Roles on userRole.RoleId equals identityRole.Id
        where identityRole.NormalizedName == role
        select userRole.UserId;
}
