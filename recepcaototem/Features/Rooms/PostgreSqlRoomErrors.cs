using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace recepcaototem.Features.Rooms;

internal static class PostgreSqlRoomErrors
{
    private const string UniqueNameIndex = "UX_Rooms_NormalizedName";

    public static bool IsNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueNameIndex
        };
}
