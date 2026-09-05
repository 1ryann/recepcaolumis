using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace recepcaototem.Features.Rooms;

internal static class SqlServerRoomErrors
{
    private const string UniqueNameIndex = "UX_Rooms_NormalizedName";

    public static bool IsNameConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        sql.Message.Contains(UniqueNameIndex, StringComparison.Ordinal);
}
