using System.Text.Json;
using Npgsql;

namespace GestaoPredio.AdminCli.Tests;

internal sealed class LocalPostgreSqlTestDatabase : IAsyncDisposable
{
    private const string UserSecretsId = "aspnet-recepcaototem-0d5d7097-983f-4449-9f40-9f998d90c348";
    private const string SchemaPrefix = "lumis_test_";
    private readonly string _baseConnection;

    private LocalPostgreSqlTestDatabase(string baseConnection, string schemaName)
    {
        _baseConnection = baseConnection;
        SchemaName = schemaName;
        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            SearchPath = $"{schemaName},extensions,public"
        }.ConnectionString;
    }

    public string SchemaName { get; }
    public string ConnectionString { get; }

    public static async Task<LocalPostgreSqlTestDatabase> CreateAsync()
    {
        var baseConnection = LoadBaseConnection();
        Validate(baseConnection);
        var database = new LocalPostgreSqlTestDatabase(
            baseConnection, $"{SchemaPrefix}{Guid.NewGuid():N}");
        await database.ExecuteSchemaCommandAsync($"CREATE SCHEMA \"{database.SchemaName}\"");
        return database;
    }

    public async ValueTask DisposeAsync() =>
        await ExecuteSchemaCommandAsync($"DROP SCHEMA IF EXISTS \"{SchemaName}\" CASCADE");

    private static string LoadBaseConnection()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", UserSecretsId, "secrets.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var flat))
            return flat.GetString() ?? "";
        return document.RootElement.GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection").GetString() ?? "";
    }

    private static void Validate(string connection)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        var local = string.Equals(builder.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(builder.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(builder.Host, "::1", StringComparison.OrdinalIgnoreCase);
        if (!local || builder.Port != 5432 ||
            !string.Equals(builder.Database, "LumisDev", StringComparison.Ordinal))
            throw new InvalidOperationException("Admin CLI tests are restricted to localhost:5432/LumisDev.");
    }

    private async Task ExecuteSchemaCommandAsync(string sql)
    {
        Validate(_baseConnection);
        if (!SchemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal) ||
            SchemaName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("Refusing to modify a schema outside the isolated test namespace.");
        await using var connection = new NpgsqlConnection(_baseConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
