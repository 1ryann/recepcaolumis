using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace recepcaototem.Features.Professionals;

internal static class SqlServerProfessionalErrors
{
    private const string UniqueUserIndex = "UX_Professionals_ApplicationUserId";

    public static bool IsUserLinkConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        sql.Message.Contains(UniqueUserIndex, StringComparison.Ordinal);
}
