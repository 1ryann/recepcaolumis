using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace recepcaototem.Features.Professionals;

internal static class PostgreSqlProfessionalErrors
{
    private const string UniqueUserIndex = "UX_Professionals_ApplicationUserId";

    public static bool IsUserLinkConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueUserIndex
        };
}
