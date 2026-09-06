using Npgsql;

namespace GestaoPredio.AdminCli;

public static class ConnectionStringGuard
{
    public static void Validate(string connection, string environment)
    {
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connection); }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("DefaultConnection must be a valid PostgreSQL connection string.", exception);
        }

        if ((string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase)) &&
            ((!string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(parsed.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(parsed.Host, "::1", StringComparison.OrdinalIgnoreCase)) ||
             parsed.Port != 5432 ||
             !string.Equals(parsed.Database, "LumisDev", StringComparison.Ordinal)))
            throw new InvalidOperationException("Development and Testing are restricted to localhost:5432/LumisDev.");
    }
}
