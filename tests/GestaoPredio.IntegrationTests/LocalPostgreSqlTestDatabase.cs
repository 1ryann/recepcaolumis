using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GestaoPredio.IntegrationTests;

internal static class LocalPostgreSqlTestDatabase
{
    public const string DatabaseName = "LumisDev";
    public const string SchemaPrefix = "lumis_test_";

    public static string LoadBaseConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(recepcaototem.Pages.IndexModel).Assembly, optional: false)
            .Build();
        var connection = configuration.GetConnectionString("DefaultConnection");
        Validate(connection);
        return connection!;
    }

    public static string WithSchema(string baseConnection, string schema)
    {
        Validate(baseConnection);
        ValidateSchema(schema);
        var builder = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            SearchPath = $"{schema},extensions,public"
        };
        return builder.ConnectionString;
    }

    public static void Validate(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("PostgreSQL tests require the local Development connection secret.");

        var builder = new NpgsqlConnectionStringBuilder(connection);
        if (!IsLoopback(builder.Host) || builder.Port != 5432 ||
            !string.Equals(builder.Database, DatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("PostgreSQL tests are restricted to localhost:5432/LumisDev.");
    }

    public static async Task CreateSchemaAsync(string baseConnection, string schema)
    {
        Validate(baseConnection);
        ValidateSchema(schema);
        await using var connection = new NpgsqlConnection(baseConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{schema}\"";
        await command.ExecuteNonQueryAsync();
    }

    public static async Task DropSchemaAsync(string baseConnection, string schema)
    {
        Validate(baseConnection);
        ValidateSchema(schema);
        await using var connection = new NpgsqlConnection(baseConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static bool IsLoopback(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSchema(string schema)
    {
        if (!schema.StartsWith(SchemaPrefix, StringComparison.Ordinal) ||
            schema.Length != SchemaPrefix.Length + 32 ||
            schema.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("Refusing to modify a schema outside the isolated test namespace.");
    }
}
