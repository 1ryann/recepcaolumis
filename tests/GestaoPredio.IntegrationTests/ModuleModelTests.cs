using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GestaoPredio.IntegrationTests;

public sealed class ModuleModelTests
{
    [Fact]
    public void Professional_columns_preserve_input_and_expanded_search_keys()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Professional>(db);
        Assert.Equal("Professionals", entity.GetTableName());
        AssertColumn(entity, "Name", "nvarchar(200)", 200);
        AssertColumn(entity, "NormalizedName", "nvarchar(400)", 400);
        AssertColumn(entity, "Profession", "nvarchar(150)", 150);
        AssertColumn(entity, "NormalizedProfession", "nvarchar(300)", 300);
        AssertColumn(entity, "WhatsApp", "varchar(16)", 16);
        Assert.False(entity.FindProperty("WhatsApp")!.IsUnicode());
        AssertColumn(entity, "ApplicationUserId", "nvarchar(450)", 450, nullable: true);
        Assert.True(entity.FindProperty("PhotoFileId")!.IsNullable);
        AssertRowVersion(entity);
    }

    [Fact]
    public void Room_columns_and_checks_preserve_rates_and_permanent_name_uniqueness()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Room>(db);
        Assert.Equal("Rooms", entity.GetTableName());
        AssertColumn(entity, "Name", "nvarchar(100)", 100);
        AssertColumn(entity, "NormalizedName", "nvarchar(200)", 200);
        AssertColumn(entity, "Description", "nvarchar(1000)", 1000, nullable: true);
        foreach (var name in new[] { "HourlyRate", "DailyRate" })
        {
            var rate = entity.FindProperty(name)!;
            Assert.Equal("decimal(18,2)", rate.GetColumnType());
            Assert.Equal(18, rate.GetPrecision());
            Assert.Equal(2, rate.GetScale());
            Assert.False(rate.IsNullable);
            Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == $"[{name}] >= 0");
        }
        AssertIndex(entity, "UX_Rooms_NormalizedName", true, null, "NormalizedName");
        AssertRowVersion(entity);
    }

    [Fact]
    public void Professional_links_are_optional_exclusive_and_never_cascade()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<Professional>(db);
        AssertIndex(entity, "UX_Professionals_ApplicationUserId", true, "[ApplicationUserId] IS NOT NULL", "ApplicationUserId");
        AssertIndex(entity, "UX_Professionals_PhotoFileId", true, "[PhotoFileId] IS NOT NULL", "PhotoFileId");
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
        AssertColumn(entity, "StorageKey", "varchar(64)", 64);
        AssertColumn(entity, "MimeType", "varchar(20)", 20);
        AssertColumn(entity, "Purpose", "varchar(50)", 50);
        Assert.Equal("bigint", entity.FindProperty("Length")!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == "[Purpose] = 'PROFESSIONAL_PHOTO'");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == "[Length] > 0");
        AssertIndex(entity, "UX_PrivateFiles_StorageKey", true, null, "StorageKey");
    }

    [Fact]
    public void Audit_additions_are_optional_and_preserve_existing_indexes_without_foreign_keys()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = Entity<AuditEntry>(db);
        AssertColumn(entity, "TargetEntityType", "nvarchar(50)", 50, nullable: true);
        AssertColumn(entity, "ChangedFields", "nvarchar(500)", 500, nullable: true);
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

    private static IEntityType Entity<T>(ApplicationDbContext db) =>
        Assert.IsAssignableFrom<IEntityType>(db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T)));

    private static void AssertColumn(IEntityType entity, string name, string type, int length, bool nullable = false)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(name));
        Assert.Equal(type, property.GetColumnType());
        Assert.Equal(length, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertRowVersion(IEntityType entity)
    {
        var property = entity.FindProperty("RowVersion")!;
        Assert.Equal("rowversion", property.GetColumnType());
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
