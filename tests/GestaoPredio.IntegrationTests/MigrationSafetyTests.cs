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
    public void Room_rental_migration_contains_exactly_two_new_tables_and_only_the_allowed_changes()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            x => x.Key.EndsWith("_RoomPhotosAndRentalInquiries", StringComparison.Ordinal));
        Assert.True(string.CompareOrdinal(metadata.Key, "20260910215630_TotemBookingHandoff") > 0);
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");
        var newTables = new[] { "RoomPhotos", "RoomRentalInquiries" };
        Assert.Equal(newTables, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order().ToArray());
        var drop = Assert.Single(migration.UpOperations.OfType<DropCheckConstraintOperation>());
        Assert.Equal("PrivateFiles", drop.Table);
        Assert.Equal("CK_PrivateFiles_Purpose", drop.Name);
        var purpose = Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>(), x => x.Table == "PrivateFiles");
        Assert.Equal("CK_PrivateFiles_Purpose", purpose.Name);
        Assert.Equal("\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')", purpose.Sql);
        Assert.All(migration.UpOperations, operation =>
        {
            switch (operation)
            {
                case CreateTableOperation table: Assert.Contains(table.Name, newTables); break;
                case CreateIndexOperation index: Assert.Contains(index.Table, newTables); break;
                case AddForeignKeyOperation foreignKey: Assert.Contains(foreignKey.Table, newTables); break;
                case AddCheckConstraintOperation check when check.Table != "PrivateFiles": Assert.Contains(check.Table, newTables); break;
                default: Assert.True(ReferenceEquals(operation, drop) || ReferenceEquals(operation, purpose),
                    $"Unexpected migration operation: {operation.GetType().Name}"); break;
            }
            Assert.False(operation.IsDestructiveChange);
        });
        var inquiry = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), x => x.Name == "RoomRentalInquiries");
        Assert.Equal(ReferentialAction.NoAction, Assert.Single(inquiry.ForeignKeys, x => x.PrincipalTable == "Leases").OnDelete);
        Assert.Equal(newTables, migration.DownOperations.OfType<DropTableOperation>().Select(x => x.Name).Order().ToArray());
        Assert.All(migration.DownOperations, operation => Assert.Contains(operation.GetType(),
            new[] { typeof(DropTableOperation), typeof(DropCheckConstraintOperation), typeof(AddCheckConstraintOperation) }));
        var restoredPurpose = Assert.Single(migration.DownOperations.OfType<AddCheckConstraintOperation>());
        Assert.Equal("PrivateFiles", restoredPurpose.Table);
        Assert.Equal("CK_PrivateFiles_Purpose", restoredPurpose.Name);
        Assert.Equal("\"Purpose\" = 'PROFESSIONAL_PHOTO'", restoredPurpose.Sql);
    }

    [Fact]
    public void WhatsApp_migration_creates_only_its_own_table_with_the_unique_wamid_index()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_AddWhatsAppMessages", StringComparison.Ordinal));
        Assert.True(string.CompareOrdinal(metadata.Key, "20260915231152_AddDesiredDatesToRoomRentalInquiry") > 0);
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("WhatsAppMessages", table.Name);
        Assert.Empty(table.ForeignKeys);
        Assert.Equal(["CK_WhatsAppMessages_Direction", "CK_WhatsAppMessages_FailureFields", "CK_WhatsAppMessages_Status"],
            table.CheckConstraints.Select(x => x.Name).Order().ToArray());
        Assert.DoesNotContain(table.Columns, x =>
            x.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Body", StringComparison.OrdinalIgnoreCase));
        var unique = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>(), x => x.IsUnique);
        Assert.Equal("UX_WhatsAppMessages_MessageId", unique.Name);
        Assert.Equal(["MessageId"], unique.Columns);
        Assert.All(migration.UpOperations, operation =>
        {
            switch (operation)
            {
                case CreateTableOperation created: Assert.Equal("WhatsAppMessages", created.Name); break;
                case CreateIndexOperation index: Assert.Equal("WhatsAppMessages", index.Table); break;
                default: Assert.Fail($"Unexpected migration operation: {operation.GetType().Name}"); break;
            }
            Assert.False(operation.IsDestructiveChange);
        });
        var dropped = Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
        Assert.Equal("WhatsAppMessages", dropped.Name);
        Assert.Single(migration.DownOperations);
    }

    [Fact]
    public void Room_rental_SQL_preserves_existing_columns_and_history()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript("TotemBookingHandoff", "RoomPhotosAndRentalInquiries",
            MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[] { "DROP TABLE", "DROP COLUMN", "ALTER COLUMN", "TRUNCATE ", "DELETE FROM",
                     "ALTER TABLE \"Rooms\"", "ALTER TABLE \"Leases\"", "ALTER TABLE \"Professionals\"", "ALTER DATABASE", "UPDATE " })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE \"RoomPhotos\"", sql);
        Assert.Contains("CREATE TABLE \"RoomRentalInquiries\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"UX_RoomPhotos_Room_Cover\" ON \"RoomPhotos\" (\"RoomId\") WHERE \"IsCover\"", sql);
        Assert.Contains("CREATE INDEX \"IX_RoomRentalInquiries_Status_CreatedAt\" ON \"RoomRentalInquiries\" (\"Status\", \"CreatedAt\") WHERE \"Status\" = 'NEW'", sql);
        Assert.Contains("ALTER TABLE \"PrivateFiles\" DROP CONSTRAINT \"CK_PrivateFiles_Purpose\"", sql);
        Assert.Contains("CHECK (\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO'))", sql);
    }

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

    [Fact]
    public void Professional_availability_migration_is_additive_and_preserves_existing_professionals()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var assembly = db.GetService<IMigrationsAssembly>();
        var metadata = Assert.Single(assembly.Migrations,
            pair => pair.Key.EndsWith("_ProfessionalAvailability", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.NotEmpty(migration.UpOperations);
        Assert.All(migration.UpOperations, operation =>
        {
            Assert.Contains(operation.GetType(), new[]
            {
                typeof(AddColumnOperation), typeof(AddCheckConstraintOperation),
                typeof(CreateTableOperation), typeof(CreateIndexOperation)
            });
            Assert.False(operation.IsDestructiveChange);
        });
        var mode = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("Professionals", mode.Table);
        Assert.Equal("AvailabilityMode", mode.Name);
        Assert.Equal((short)0, mode.DefaultValue);
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(["ProfessionalAvailabilityExceptions", "ProfessionalAvailabilityIntervals"],
            tables.Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.All(tables.SelectMany(value => value.ForeignKeys), foreignKey =>
            Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));

        var sql = db.GetService<IMigrator>().GenerateScript(
            "ProfessionalRegistrationRequests", "ProfessionalAvailability",
            MigrationsSqlGenerationOptions.Idempotent);
        foreach (var forbidden in new[]
                 {
                     "DROP ", "TRUNCATE ", "DELETE FROM", "ALTER COLUMN", "ALTER DATABASE",
                     "sp_getapplock", "rowversion", "CREATE TABLE \"Slots\""
                 })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADD \"AvailabilityMode\" smallint NOT NULL DEFAULT 0", sql);
        Assert.Contains("CREATE TABLE \"ProfessionalAvailabilityIntervals\"", sql);
        Assert.Contains("CREATE TABLE \"ProfessionalAvailabilityExceptions\"", sql);
    }
}
