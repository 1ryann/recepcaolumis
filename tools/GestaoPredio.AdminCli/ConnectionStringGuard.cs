using Microsoft.Data.SqlClient;

namespace GestaoPredio.AdminCli;

public static class ConnectionStringGuard
{
    public static void Validate(string connection, string environment)
    {
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required.");
        var parsed = new SqlConnectionStringBuilder(connection);
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) &&
            (!parsed.IntegratedSecurity || !string.IsNullOrEmpty(parsed.UserID) || !string.IsNullOrEmpty(parsed.Password)))
            throw new InvalidOperationException("Production requires Windows integrated database authentication.");
    }
}
