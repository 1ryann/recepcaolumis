using Microsoft.Extensions.Configuration;

namespace GestaoPredio.DataMigration;

public sealed record MigrationSettings(
    string? SourceConnection,
    string? TargetConnection,
    string? TargetProject,
    string? TargetEnvironment,
    string? TargetFingerprint)
{
    public static MigrationSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(MigrationSettings).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return new MigrationSettings(
            configuration["Migration:SourceConnection"],
            configuration["Migration:TargetConnection"],
            configuration["Migration:TargetProject"],
            configuration["Migration:TargetEnvironment"],
            configuration["Migration:TargetFingerprint"]);
    }

    public string RequireSource() => Require(SourceConnection, "Migration:SourceConnection");
    public string RequireTarget() => Require(TargetConnection, "Migration:TargetConnection");

    public TargetIdentity ValidateTarget() => MigrationGuard.ValidateTarget(
        RequireTarget(), Require(TargetProject, "Migration:TargetProject"),
        Require(TargetEnvironment, "Migration:TargetEnvironment"),
        Require(TargetFingerprint, "Migration:TargetFingerprint"));

    private static string Require(string? value, string key) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new MigrationSafetyException($"Missing external configuration: {key}.");
}
