using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace GestaoPredio.DataMigration;

public sealed class MigrationSafetyException(string message) : InvalidOperationException(message);

public sealed record SourceIdentity(string Provider, string Database, string SafeDescription);

public sealed record TargetIdentity(string Provider, string Project, string Environment, string Fingerprint,
    string SafeDescription);

public static class MigrationGuard
{
    public const string ApprovedSourceDatabase = "GestaoPredioDB";
    public const string ApprovedTargetProject = "Lumis";
    public const string ApprovedTargetEnvironment = "Production";

    public static SourceIdentity ValidateSource(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(Require(connectionString, "source connection"));
        }
        catch (ArgumentException exception)
        {
            throw new MigrationSafetyException($"The source must be a valid SQL Server connection: {exception.GetType().Name}.");
        }

        if (!string.Equals(builder.InitialCatalog, ApprovedSourceDatabase, StringComparison.Ordinal))
            throw new MigrationSafetyException($"Source database must be exactly {ApprovedSourceDatabase}.");

        return new SourceIdentity("SQL Server", builder.InitialCatalog,
            $"SQL Server / {ApprovedSourceDatabase}");
    }

    public static TargetIdentity ValidateTarget(string connectionString, string project, string environment,
        string expectedFingerprint)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(Require(connectionString, "target connection"));
        }
        catch (ArgumentException exception)
        {
            throw new MigrationSafetyException($"The target must be a valid PostgreSQL connection: {exception.GetType().Name}.");
        }

        if (!string.Equals(project, ApprovedTargetProject, StringComparison.Ordinal) ||
            !string.Equals(environment, ApprovedTargetEnvironment, StringComparison.Ordinal))
            throw new MigrationSafetyException("Target project and environment are not the approved Lumis Production target.");

        var host = Require(builder.Host ?? string.Empty, "target host");
        var database = Require(builder.Database ?? string.Empty, "target database");
        var username = Require(builder.Username ?? string.Empty, "target username");
        if (IsLocal(host) || string.Equals(database, "LumisDev", StringComparison.OrdinalIgnoreCase))
            throw new MigrationSafetyException("A local or LumisDev database cannot be used as the production target.");

        if (!IsSupabaseHost(host))
            throw new MigrationSafetyException("The production target must use an approved Supabase PostgreSQL host.");

        var actualFingerprint = ComputeTargetFingerprint(connectionString);
        if (string.IsNullOrWhiteSpace(expectedFingerprint) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualFingerprint),
                Encoding.ASCII.GetBytes(expectedFingerprint.Trim().ToUpperInvariant())))
            throw new MigrationSafetyException("Target fingerprint does not match the explicitly approved target.");

        return new TargetIdentity("PostgreSQL", project, environment, actualFingerprint,
            $"PostgreSQL / Supabase Production / Lumis / Fingerprint={actualFingerprint[..12]}");
    }

    public static string ComputeTargetFingerprint(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(Require(connectionString, "target connection"));
        var identity = string.Join('|', Require(builder.Host ?? string.Empty, "target host").Trim().ToLowerInvariant(),
            builder.Port, Require(builder.Database ?? string.Empty, "target database").Trim().ToLowerInvariant(),
            Require(builder.Username ?? string.Empty, "target username").Trim().ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static bool IsLocal(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1";

    private static bool IsSupabaseHost(string host) =>
        host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".supabase.com", StringComparison.OrdinalIgnoreCase);

    private static string Require(string value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new MigrationSafetyException($"Missing {name}.");
}
