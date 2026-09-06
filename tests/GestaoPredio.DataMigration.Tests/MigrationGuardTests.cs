using GestaoPredio.DataMigration;

namespace GestaoPredio.DataMigration.Tests;

public sealed class MigrationGuardTests
{
    [Fact]
    public void Source_requires_the_production_sql_server_database()
    {
        var valid = MigrationGuard.ValidateSource(
            "Server=SOPH-SISPONTO\\SQLEXPRESS;Database=GestaoPredioDB;Integrated Security=true;TrustServerCertificate=true");

        Assert.Equal("GestaoPredioDB", valid.Database);
        Assert.Throws<MigrationSafetyException>(() => MigrationGuard.ValidateSource(
            "Server=localhost;Database=LumisDev;Integrated Security=true;TrustServerCertificate=true"));
        Assert.Throws<MigrationSafetyException>(() => MigrationGuard.ValidateSource(
            "Server=localhost;Database=GestaoPredioHomolog;Integrated Security=true;TrustServerCertificate=true"));
    }

    [Fact]
    public void Target_rejects_localhost_lumisdev_and_requires_the_approved_fingerprint()
    {
        const string remote = "Host=aws-0-sa-east-1.pooler.supabase.com;Database=postgres;Username=postgres.projectref;Password=secret;SSL Mode=Require";
        var fingerprint = MigrationGuard.ComputeTargetFingerprint(remote);

        var valid = MigrationGuard.ValidateTarget(remote, "Lumis", "Production", fingerprint);

        Assert.Equal("PostgreSQL", valid.Provider);
        Assert.Equal("Lumis", valid.Project);
        Assert.DoesNotContain("secret", valid.SafeDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<MigrationSafetyException>(() => MigrationGuard.ValidateTarget(
            "Host=localhost;Database=LumisDev;Username=postgres;Password=secret", "Lumis", "Production", fingerprint));
        Assert.Throws<MigrationSafetyException>(() => MigrationGuard.ValidateTarget(remote, "Other", "Production", fingerprint));
        Assert.Throws<MigrationSafetyException>(() => MigrationGuard.ValidateTarget(remote, "Lumis", "Production", "wrong"));
    }

    [Fact]
    public void Target_fingerprint_is_deterministic_and_does_not_include_the_password()
    {
        const string first = "Host=db.example.supabase.co;Database=postgres;Username=postgres.project;Password=one;SSL Mode=Require";
        const string second = "Host=db.example.supabase.co;Database=postgres;Username=postgres.project;Password=two;SSL Mode=Require";

        Assert.Equal(MigrationGuard.ComputeTargetFingerprint(first), MigrationGuard.ComputeTargetFingerprint(second));
        Assert.Matches("^[A-F0-9]{64}$", MigrationGuard.ComputeTargetFingerprint(first));
    }
}
