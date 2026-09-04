using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GestaoPredio.IntegrationTests;

public sealed class MigrationSafetyTests
{
    [Fact]
    public void Auth_model_has_required_additive_columns()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var user = db.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.Equal(200, user.FindProperty(nameof(ApplicationUser.DisplayName))!.GetMaxLength());
        Assert.False(user.FindProperty(nameof(ApplicationUser.IsActive))!.IsNullable);
        Assert.False(user.FindProperty(nameof(ApplicationUser.MustChangePassword))!.IsNullable);

        var audit = db.Model.FindEntityType(typeof(AuditEntry))!;
        Assert.Equal(450, audit.FindProperty(nameof(AuditEntry.TargetUserId))!.GetMaxLength());
        Assert.Equal(45, audit.FindProperty(nameof(AuditEntry.IpAddress))!.GetMaxLength());
        Assert.Contains(audit.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AuditEntry.Action), nameof(AuditEntry.OccurredAt)]));
    }

    [Fact]
    public void Forward_auth_migration_is_additive()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20260904174759_InfrastructureFoundation",
            "20260904235115_AuthenticationAndProvisioning",
            MigrationsSqlGenerationOptions.Idempotent);

        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE [AspNetUsers]", sql);
        Assert.Contains("ALTER TABLE [AuditEntries]", sql);
        Assert.Contains("CREATE INDEX [IX_AuditEntries_Action_OccurredAt]", sql);
    }
}
