using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GestaoPredio.IntegrationTests;

public sealed class ModuleModelTests
{
    [Fact]
    public void Design_time_factory_uses_connection_from_process_environment_when_present()
    {
        const string key = "ConnectionStrings__DefaultConnection";
        const string connection = "Host=localhost;Port=5432;Database=LumisDev;Username=test;Password=not-used";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, connection);
            using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

            Assert.Equal(connection, db.Database.GetConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public void Professional_columns_preserve_input_and_expanded_search_keys()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Professional>(db);
        Assert.Equal("Professionals", entity.GetTableName());
        AssertColumn(entity, "Name", "character varying(200)", 200);
        AssertColumn(entity, "NormalizedName", "character varying(400)", 400);
        AssertColumn(entity, "Profession", "character varying(150)", 150);
        AssertColumn(entity, "NormalizedProfession", "character varying(300)", 300);
        AssertColumn(entity, "WhatsApp", "character varying(16)", 16);
        Assert.False(entity.FindProperty("WhatsApp")!.IsUnicode());
        AssertColumn(entity, "ApplicationUserId", "character varying(450)", 450, nullable: true);
        Assert.True(entity.FindProperty("PhotoFileId")!.IsNullable);
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Professional_availability_model_uses_civil_types_non_destructive_links_and_xmin()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var professional = Entity<Professional>(db);
        Assert.Equal("smallint", professional.FindProperty("AvailabilityMode")!.GetColumnType());
        Assert.Contains(professional.GetCheckConstraints(), check => check.Name == "CK_Professionals_AvailabilityMode");

        var interval = Entity<ProfessionalAvailabilityInterval>(db);
        Assert.Equal("ProfessionalAvailabilityIntervals", interval.GetTableName());
        Assert.Equal("smallint", interval.FindProperty("DayOfWeek")!.GetColumnType());
        Assert.Equal("time without time zone", interval.FindProperty("StartTime")!.GetColumnType());
        Assert.Equal("time without time zone", interval.FindProperty("EndTime")!.GetColumnType());
        Assert.Contains(interval.GetCheckConstraints(), check => check.Name == "CK_ProfessionalAvailabilityIntervals_Period");
        Assert.Equal(DeleteBehavior.NoAction, Assert.Single(interval.GetForeignKeys()).DeleteBehavior);
        AssertIndex(interval, "UX_ProfessionalAvailabilityIntervals_Professional_Day_Start", true, null,
            "ProfessionalId", "DayOfWeek", "StartTime");

        var exception = Entity<ProfessionalAvailabilityException>(db);
        Assert.Equal("ProfessionalAvailabilityExceptions", exception.GetTableName());
        Assert.Equal("date", exception.FindProperty("Date")!.GetColumnType());
        Assert.Equal("time without time zone", exception.FindProperty("StartTime")!.GetColumnType());
        Assert.Equal("time without time zone", exception.FindProperty("EndTime")!.GetColumnType());
        Assert.Equal("character varying(300)", exception.FindProperty("Reason")!.GetColumnType());
        Assert.Contains(exception.GetCheckConstraints(), check => check.Name == "CK_ProfessionalAvailabilityExceptions_Shape");
        Assert.Equal(DeleteBehavior.NoAction, Assert.Single(exception.GetForeignKeys()).DeleteBehavior);
        AssertIndex(exception, "IX_ProfessionalAvailabilityExceptions_Professional_Date_Start", false, null,
            "ProfessionalId", "Date", "AllDay", "StartTime");
        AssertIndex(exception, "UX_ProfessionalAvailabilityExceptions_Professional_Date_AllDay", true,
            "\"AllDay\" = TRUE", "ProfessionalId", "Date");
        AssertIndex(exception, "UX_ProfessionalAvailabilityExceptions_Professional_Date_Start", true,
            "\"AllDay\" = FALSE", "ProfessionalId", "Date", "StartTime");
        AssertPostgreSqlVersion(exception);
    }

    [Fact]
    public void Room_columns_and_checks_preserve_rates_and_permanent_name_uniqueness()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Room>(db);
        Assert.Equal("Rooms", entity.GetTableName());
        AssertColumn(entity, "Name", "character varying(100)", 100);
        AssertColumn(entity, "NormalizedName", "character varying(200)", 200);
        AssertColumn(entity, "Description", "character varying(1000)", 1000, nullable: true);
        foreach (var name in new[] { "HourlyRate", "DailyRate" })
        {
            var rate = entity.FindProperty(name)!;
            Assert.Equal("numeric(18,2)", rate.GetColumnType());
            Assert.Equal(18, rate.GetPrecision());
            Assert.Equal(2, rate.GetScale());
            Assert.False(rate.IsNullable);
            Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == $"\"{name}\" >= 0");
        }
        AssertIndex(entity, "UX_Rooms_NormalizedName", true, null, "NormalizedName");
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Professional_links_are_optional_exclusive_and_never_cascade()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Professional>(db);
        AssertIndex(entity, "UX_Professionals_ApplicationUserId", true, null, "ApplicationUserId");
        AssertIndex(entity, "UX_Professionals_PhotoFileId", true, null, "PhotoFileId");
        Assert.Equal(2, entity.GetIndexes().Count(index => index.IsUnique));
        var foreignKeys = entity.GetForeignKeys().ToArray();
        Assert.Equal(2, foreignKeys.Length);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ApplicationUser)
            && foreignKey.Properties.Single().Name == "ApplicationUserId");
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(PrivateFile)
            && foreignKey.Properties.Single().Name == "PhotoFileId");
    }

    [Fact]
    public void Private_file_metadata_has_bounded_columns_and_controlled_purpose()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<PrivateFile>(db);
        Assert.Equal("PrivateFiles", entity.GetTableName());
        AssertColumn(entity, "StorageKey", "character varying(64)", 64);
        AssertColumn(entity, "MimeType", "character varying(20)", 20);
        AssertColumn(entity, "Purpose", "character varying(50)", 50);
        Assert.Equal("bigint", entity.FindProperty("Length")!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_PrivateFiles_Purpose"
            && check.Sql == "\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == "\"Length\" > 0");
        AssertIndex(entity, "UX_PrivateFiles_StorageKey", true, null, "StorageKey");
    }

    [Fact]
    public void Audit_additions_are_optional_and_preserve_existing_indexes_without_foreign_keys()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<AuditEntry>(db);
        AssertColumn(entity, "TargetEntityType", "character varying(50)", 50, nullable: true);
        AssertColumn(entity, "ChangedFields", "character varying(500)", 500, nullable: true);
        Assert.True(entity.FindProperty("TargetEntityId")!.IsNullable);
        AssertIndex(entity, "IX_AuditEntries_TargetEntity", false, null, "TargetEntityType", "TargetEntityId", "OccurredAt");
        AssertIndex(entity, "IX_AuditEntries_OccurredAt", false, null, "OccurredAt");
        AssertIndex(entity, "IX_AuditEntries_Action_OccurredAt", false, null, "Action", "OccurredAt");
        Assert.Empty(entity.GetForeignKeys());
    }

    [Fact]
    public void Expanded_unicode_at_each_input_limit_fits_its_derived_column()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var professional = Professional.Create(new string('ß', 200), new string('ß', 150), "65999999999", DateTimeOffset.UtcNow);
        var room = Room.Create(new string('ß', 100), null, 0, 0, DateTimeOffset.UtcNow);
        Assert.Equal(new string('S', 400), professional.NormalizedName);
        Assert.Equal(new string('S', 300), professional.NormalizedProfession);
        Assert.Equal(new string('S', 200), room.NormalizedName);
        Assert.True(professional.NormalizedName.Length <= Entity<Professional>(db).FindProperty("NormalizedName")!.GetMaxLength());
        Assert.True(professional.NormalizedProfession.Length <= Entity<Professional>(db).FindProperty("NormalizedProfession")!.GetMaxLength());
        Assert.True(room.NormalizedName.Length <= Entity<Room>(db).FindProperty("NormalizedName")!.GetMaxLength());
    }

    [Fact]
    public void Tenant_model_uses_bounded_columns_checks_and_postgresql_concurrency()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Tenant>(db);

        Assert.Equal("Tenants", entity.GetTableName());
        AssertColumn(entity, "Name", "character varying(200)", 200);
        AssertColumn(entity, "NormalizedName", "character varying(400)", 400);
        AssertColumn(entity, "Kind", "character varying(20)", 20);
        Assert.Equal("timestamp with time zone", entity.FindProperty("CreatedAt")!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty("UpdatedAt")!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_Tenants_Kind");
        AssertIndex(entity, "IX_Tenants_NormalizedName", false, null, "NormalizedName", "Id");
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Lease_model_has_controlled_values_checks_no_action_links_and_query_indexes()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Lease>(db);

        Assert.Equal("Leases", entity.GetTableName());
        AssertColumn(entity, "Mode", "character varying(10)", 10);
        AssertColumn(entity, "LifecycleState", "character varying(20)", 20);
        var rate = entity.FindProperty("ContractedRate")!;
        Assert.Equal("numeric(18,2)", rate.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty("BillingStartAt")!.GetColumnType());
        Assert.Equal("smallint", entity.FindProperty("BillingDueDay")!.GetColumnType());
        Assert.Equal("smallint", entity.FindProperty("MonthlyAnchorDay")!.GetColumnType());
        Assert.Equal(5, entity.GetCheckConstraints().Count());
        Assert.Equal(3, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        AssertIndex(entity, "IX_Leases_Room_State_Start", false, null, "RoomId", "LifecycleState", "OccupancyStartAt");
        AssertIndex(entity, "IX_Leases_Professional_State_Start", false, null, "ProfessionalId", "LifecycleState", "OccupancyStartAt");
        AssertIndex(entity, "IX_Leases_Tenant_State_Billing", false, null, "TenantId", "LifecycleState", "BillingStartAt");
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Lease_occurrence_model_is_unique_per_start_and_never_cascades()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<LeaseOccurrence>(db);

        Assert.Equal("LeaseOccurrences", entity.GetTableName());
        AssertColumn(entity, "State", "character varying(20)", 20);
        AssertIndex(entity, "UX_LeaseOccurrences_LeaseId_StartAt", true, null, "LeaseId", "StartAt");
        Assert.Equal(DeleteBehavior.NoAction, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Financial_charge_model_freezes_amounts_and_uses_non_destructive_links()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<FinancialCharge>(db);
        Assert.Equal("FinancialCharges", entity.GetTableName());
        Assert.Equal("numeric(18,2)", entity.FindProperty(nameof(FinancialCharge.CalculatedAmount))!.GetColumnType());
        Assert.Equal("numeric(18,2)", entity.FindProperty(nameof(FinancialCharge.FinalAmount))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(FinancialCharge.DueDate))!.GetColumnType());
        Assert.Equal(4000, entity.FindProperty(nameof(FinancialCharge.CalculationDetails))!.GetMaxLength());
        AssertIndex(entity, "UX_FinancialCharges_Lease_Period", true, null, "LeaseId", "ReferencePeriodStart", "ReferencePeriodEnd");
        Assert.All(entity.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Reservation_model_preserves_workflow_history_and_supports_resource_conflict_queries()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Reservation>(db);

        Assert.Equal("Reservations", entity.GetTableName());
        AssertColumn(entity, "Kind", "character varying(20)", 20);
        AssertColumn(entity, "Status", "character varying(20)", 20);
        AssertColumn(entity, "RequestedByUserId", "character varying(450)", 450);
        AssertColumn(entity, "DecidedByUserId", "character varying(450)", 450, nullable: true);
        AssertColumn(entity, "RejectionReason", "character varying(500)", 500, nullable: true);
        Assert.Equal("timestamp with time zone", entity.FindProperty("StartAt")!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty("EndAt")!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_Reservations_Kind");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_Reservations_Status");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_Reservations_Period");
        Assert.Equal(4, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        AssertIndex(entity, "IX_Reservations_Room_Status_Start", false, null, "RoomId", "Status", "StartAt");
        AssertIndex(entity, "IX_Reservations_Professional_Status_Start", false, null, "ProfessionalId", "Status", "StartAt");
        AssertIndex(entity, "IX_Reservations_OriginalReservationId", false, null, "OriginalReservationId");
        AssertPostgreSqlVersion(entity);
    }

    [Fact]
    public void Visit_model_preserves_state_history_and_uses_postgresql_concurrency()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var visit = Entity<Visit>(db);
        Assert.Equal("Visits", visit.GetTableName());
        AssertColumn(visit, "VisitorName", "character varying(200)", 200);
        AssertColumn(visit, "Status", "character varying(20)", 20);
        Assert.True(visit.FindProperty("RoomId")!.IsNullable);
        Assert.True(visit.FindProperty("ReservationId")!.IsNullable);
        Assert.Equal(4, visit.GetForeignKeys().Count());
        Assert.All(visit.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        AssertIndex(visit, "IX_Visits_Status_ArrivedAt", false, null, "Status", "ArrivedAt");
        AssertIndex(visit, "IX_Visits_Professional_Status_ArrivedAt", false, null,
            "ProfessionalId", "Status", "ArrivedAt");
        AssertPostgreSqlVersion(visit);

        var transition = Entity<VisitTransition>(db);
        Assert.Equal("VisitTransitions", transition.GetTableName());
        AssertColumn(transition, "ActorUserId", "character varying(450)", 450);
        AssertColumn(transition, "Reason", "character varying(500)", 500, nullable: true);
        Assert.Equal(DeleteBehavior.NoAction, Assert.Single(transition.GetForeignKeys()).DeleteBehavior);
        AssertIndex(transition, "IX_VisitTransitions_Visit_OccurredAt", false, null, "VisitId", "OccurredAt");
    }

    [Fact]
    public void Operating_hours_model_uses_local_civil_time_and_postgresql_concurrency()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var schedule = Entity<OperatingHoursSchedule>(db);
        Assert.Equal("OperatingHoursSchedules", schedule.GetTableName());
        AssertPostgreSqlVersion(schedule);

        var interval = Entity<OperatingHourInterval>(db);
        Assert.Equal("OperatingHourIntervals", interval.GetTableName());
        Assert.Equal("smallint", interval.FindProperty("DayOfWeek")!.GetColumnType());
        Assert.Equal("time without time zone", interval.FindProperty("OpensAt")!.GetColumnType());
        Assert.Equal("time without time zone", interval.FindProperty("ClosesAt")!.GetColumnType());
        Assert.Contains(interval.GetCheckConstraints(), check => check.Name == "CK_OperatingHourIntervals_Period");
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(interval.GetForeignKeys()).DeleteBehavior);
        AssertIndex(interval, "UX_OperatingHourIntervals_Schedule_Day_Open", true, null,
            "ScheduleId", "DayOfWeek", "OpensAt");
    }

    [Fact]
    public void Room_block_model_preserves_room_history_and_supports_overlap_queries()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<RoomBlock>(db);
        Assert.Equal("RoomBlocks", entity.GetTableName());
        AssertColumn(entity, "Reason", "character varying(500)", 500);
        AssertColumn(entity, "Status", "character varying(20)", 20);
        AssertColumn(entity, "CreatedBy", "character varying(450)", 450);
        AssertColumn(entity, "CancelledBy", "character varying(450)", 450, nullable: true);
        Assert.Equal("timestamp with time zone", entity.FindProperty("StartAt")!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty("EndAt")!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_RoomBlocks_Period");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_RoomBlocks_Status");
        Assert.Equal(DeleteBehavior.NoAction, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        AssertIndex(entity, "IX_RoomBlocks_Room_Status_Start", false, null,
            "RoomId", "Status", "StartAt");
        AssertIndex(entity, "IX_RoomBlocks_Room_End", false, null, "RoomId", "EndAt");
        AssertPostgreSqlVersion(entity);
    }

    private static IEntityType Entity<T>(ApplicationDbContext db) =>
        Assert.IsAssignableFrom<IEntityType>(db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T)));

    private static void AssertColumn(IEntityType entity, string name, string type, int length, bool nullable = false)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(name));
        Assert.Equal(type, property.GetColumnType());
        Assert.Equal(length, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertPostgreSqlVersion(IEntityType entity)
    {
        var property = entity.FindProperty("Version")!;
        Assert.Equal(typeof(uint), property.ClrType);
        Assert.Equal("xid", property.GetColumnType());
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.False(property.IsNullable);
    }

    private static void AssertIndex(IEntityType entity, string name, bool unique, string? filter, params string[] columns)
    {
        var index = Assert.Single(entity.GetIndexes(), index => index.GetDatabaseName() == name);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
        Assert.Equal(columns, index.Properties.Select(property => property.Name));
    }
}
