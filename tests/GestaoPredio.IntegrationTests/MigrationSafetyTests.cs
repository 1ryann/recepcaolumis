using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GestaoPredio.IntegrationTests;

public sealed class MigrationSafetyTests
{
    [Fact]
    public void Modules_migration_has_only_additive_operations_and_no_Identity_changes()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations, pair => pair.Key.EndsWith("_ProfessionalsAndRooms", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Microsoft.EntityFrameworkCore.SqlServer");
        var allowed = new[] { typeof(CreateTableOperation), typeof(AddColumnOperation), typeof(CreateIndexOperation),
            typeof(AddForeignKeyOperation), typeof(AddCheckConstraintOperation) };
        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), allowed);
            Assert.False(operation.IsDestructiveChange);
        });
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "PrivateFiles", "Professionals", "Rooms" }, tables.Select(table => table.Name).Order());
        foreach (var table in tables)
        {
            Assert.All(table.ForeignKeys, AssertNoAction);
            Assert.All(table.Columns, column => Assert.Null(column.Collation));
        }
        Assert.All(migration.UpOperations.OfType<AddForeignKeyOperation>(), AssertNoAction);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(new[] { "ChangedFields", "TargetEntityId", "TargetEntityType" }, columns.Select(column => column.Name).Order());
        Assert.All(columns, column =>
        {
            Assert.Equal("AuditEntries", column.Table);
            Assert.True(column.IsNullable);
            Assert.Null(column.Collation);
        });
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index =>
            Assert.Contains(index.Table, new[] { "PrivateFiles", "Professionals", "Rooms", "AuditEntries" }));
    }

    [Fact]
    public void Forward_modules_SQL_has_no_destructive_collation_cascade_or_Identity_operations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20260904235115_AuthenticationAndProvisioning", "ProfessionalsAndRooms");
        foreach (var forbidden in new[] { "DROP ", "TRUNCATE ", "DELETE ", "sp_rename", "ALTER DATABASE", "COLLATE", "CASCADE", "ALTER TABLE [AspNet", "CREATE TABLE [AspNet" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [Professionals]", sql);
        Assert.Contains("CREATE TABLE [PrivateFiles]", sql);
        Assert.Contains("CREATE TABLE [Rooms]", sql);
        Assert.Contains("CREATE INDEX [IX_AuditEntries_TargetEntity]", sql);
    }

    private static void AssertNoAction(AddForeignKeyOperation foreignKey)
    {
        Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
        Assert.Equal(ReferentialAction.NoAction, foreignKey.OnUpdate);
    }

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
