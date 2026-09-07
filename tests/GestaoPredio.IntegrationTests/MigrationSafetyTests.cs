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
    public void PostgreSQL_model_snapshot_is_loaded_for_future_migrations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.NotNull(db.GetService<IMigrationsAssembly>().ModelSnapshot);
    }

    [Fact]
    public void PostgreSQL_baseline_contains_only_schema_creation_operations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_PostgreSqlBaseline", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");
        var allowed = new[]
        {
            typeof(AlterDatabaseOperation), typeof(CreateTableOperation), typeof(CreateIndexOperation)
        };

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), allowed);
            Assert.False(operation.IsDestructiveChange);
        });

        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        foreach (var required in new[] { "AspNetUsers", "AspNetRoles", "AuditEntries", "PrivateFiles", "Professionals", "Rooms" })
            Assert.Contains(tables, table => table.Name == required);

        var professionals = Assert.Single(tables, table => table.Name == "Professionals");
        Assert.All(professionals.ForeignKeys, foreignKey =>
        {
            Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
            Assert.Equal(ReferentialAction.NoAction, foreignKey.OnUpdate);
        });
    }

    [Fact]
    public void PostgreSQL_baseline_SQL_has_no_destructive_or_SQL_Server_commands()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(
            fromMigration: null,
            toMigration: "PostgreSqlBaseline",
            MigrationsSqlGenerationOptions.Idempotent);

        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", " COLLATE ",
                     "sp_getapplock", "rowversion", "[AspNet", "[Professionals]"
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS unaccent SCHEMA extensions", sql);
        Assert.Contains("CREATE TABLE \"AspNetUsers\"", sql);
        Assert.Contains("CREATE TABLE \"AuditEntries\"", sql);
        Assert.Contains("CREATE TABLE \"Professionals\"", sql);
        Assert.Contains("CREATE TABLE \"PrivateFiles\"", sql);
        Assert.Contains("CREATE TABLE \"Rooms\"", sql);
        Assert.Contains("CREATE INDEX \"IX_AuditEntries_TargetEntity\"", sql);
    }

    [Fact]
    public void Auth_model_has_required_columns_and_indexes_in_baseline()
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
    public void Leases_migration_is_additive_and_scoped_to_the_new_module()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_LeasesFoundation", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[] { typeof(CreateTableOperation), typeof(CreateIndexOperation) });
            Assert.False(operation.IsDestructiveChange);
        });
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(["LeaseOccurrences", "Leases", "Tenants"],
            tables.Select(table => table.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(tables, table => table.Name.StartsWith("AspNet", StringComparison.Ordinal));

        var sql = db.GetService<IMigrator>().GenerateScript(
            "PostgreSqlBaseline", "LeasesFoundation", MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE", " COLLATE ",
                     "sp_getapplock", "rowversion", "AspNetUsers\" ALTER"
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"Tenants\"", sql);
        Assert.Contains("CREATE TABLE \"Leases\"", sql);
        Assert.Contains("CREATE TABLE \"LeaseOccurrences\"", sql);
    }

    [Fact]
    public void Reservations_migration_is_additive_and_scoped_to_the_new_module()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_ReservationsFoundation", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[] { typeof(CreateTableOperation), typeof(CreateIndexOperation) });
            Assert.False(operation.IsDestructiveChange);
        });
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("Reservations", table.Name);
        Assert.All(table.ForeignKeys, foreignKey => Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));

        var sql = db.GetService<IMigrator>().GenerateScript(
            "LeasesFoundation", "ReservationsFoundation", MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE", " COLLATE ",
                     "sp_getapplock", "rowversion", "AspNetUsers\" ALTER"
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"Reservations\"", sql);
        Assert.Contains("IX_Reservations_Room_Status_Start", sql);
        Assert.Contains("IX_Reservations_Professional_Status_Start", sql);
    }

    [Fact]
    public void Visits_migration_is_additive_and_preserves_related_history()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_VisitsFoundation", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[] { typeof(CreateTableOperation), typeof(CreateIndexOperation) });
            Assert.False(operation.IsDestructiveChange);
        });
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(["Visits", "VisitTransitions"], tables.Select(table => table.Name));
        Assert.All(tables.SelectMany(table => table.ForeignKeys), foreignKey =>
            Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));

        var sql = db.GetService<IMigrator>().GenerateScript(
            "ReservationsFoundation", "VisitsFoundation", MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE", " COLLATE ",
                     "sp_getapplock", "rowversion", "AspNetUsers\" ALTER"
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"Visits\"", sql);
        Assert.Contains("CREATE TABLE \"VisitTransitions\"", sql);
    }

    [Fact]
    public void Financial_charges_migration_is_additive_and_non_destructive()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_FinancialChargesFoundation", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");
        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[] { typeof(CreateTableOperation), typeof(CreateIndexOperation) });
            Assert.False(operation.IsDestructiveChange);
        });
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("FinancialCharges", table.Name);
        Assert.All(table.ForeignKeys, foreignKey => Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));
        var sql = db.GetService<IMigrator>().GenerateScript("OperatingHoursAndRoomBlocks", "FinancialChargesFoundation", MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[] { "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE", "sp_getapplock", "rowversion" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"FinancialCharges\"", sql);
        Assert.Contains("UX_FinancialCharges_Lease_Period", sql);
    }

    [Fact]
    public void Operating_hours_and_room_blocks_migration_is_additive_and_preserves_room_history()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_OperatingHoursAndRoomBlocks", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[] { typeof(CreateTableOperation), typeof(CreateIndexOperation) });
            Assert.False(operation.IsDestructiveChange);
        });
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(["OperatingHourIntervals", "OperatingHoursSchedules", "RoomBlocks"],
            tables.Select(table => table.Name).Order(StringComparer.Ordinal));
        var roomBlocks = Assert.Single(tables, table => table.Name == "RoomBlocks");
        Assert.Equal(ReferentialAction.NoAction, Assert.Single(roomBlocks.ForeignKeys).OnDelete);

        var sql = db.GetService<IMigrator>().GenerateScript(
            "VisitsFoundation", "OperatingHoursAndRoomBlocks", MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE", " COLLATE ",
                     "sp_getapplock", "rowversion", "AspNetUsers\" ALTER"
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"OperatingHoursSchedules\"", sql);
        Assert.Contains("CREATE TABLE \"OperatingHourIntervals\"", sql);
        Assert.Contains("CREATE TABLE \"RoomBlocks\"", sql);
    }
}
