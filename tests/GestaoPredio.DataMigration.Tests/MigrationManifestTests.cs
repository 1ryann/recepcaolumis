using GestaoPredio.DataMigration;

namespace GestaoPredio.DataMigration.Tests;

public sealed class MigrationManifestTests
{
    [Fact]
    public void Manifest_preserves_dependency_order_and_excludes_database_implementation_fields()
    {
        var tables = MigrationManifest.Tables;

        Assert.Equal("AspNetRoles", tables[0].Name);
        var names = tables.Select(x => x.Name).ToArray();
        Assert.True(Array.IndexOf(names, "PrivateFiles") < Array.IndexOf(names, "Professionals"));
        Assert.DoesNotContain(tables, x => x.Name == "__EFMigrationsHistory");
        Assert.DoesNotContain(tables.SelectMany(x => x.Columns), x => x.Equals("RowVersion", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tables.SelectMany(x => x.Columns), x => x.Equals("xmin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Manifest_contains_identity_and_current_production_entities()
    {
        var names = MigrationManifest.Tables.Select(x => x.Name).ToArray();

        Assert.Contains("AspNetUsers", names);
        Assert.Contains("AspNetUserRoles", names);
        Assert.Contains("AuditEntries", names);
        Assert.Contains("PrivateFiles", names);
        Assert.Contains("Rooms", names);
        Assert.Contains("Professionals", names);
        Assert.DoesNotContain("Tenants", names);
        Assert.DoesNotContain("Leases", names);
        Assert.DoesNotContain("LeaseOccurrences", names);
    }

    [Fact]
    public void Values_keep_decimal_and_normalize_unspecified_datetime_as_utc()
    {
        const decimal amount = 9999999999999.99m;
        var unspecified = new DateTime(2026, 9, 6, 12, 30, 0, DateTimeKind.Unspecified);

        Assert.IsType<decimal>(MigrationValue.Normalize(amount));
        Assert.Equal(amount, MigrationValue.Normalize(amount));
        var normalized = Assert.IsType<DateTime>(MigrationValue.Normalize(unspecified));
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(unspecified.Ticks, normalized.Ticks);
    }

    [Fact]
    public void Timestamps_are_truncated_to_postgresql_microsecond_precision_without_changing_the_instant()
    {
        var offset = new DateTimeOffset(2026, 9, 6, 12, 30, 0, TimeSpan.Zero).AddTicks(7);
        var dateTime = offset.UtcDateTime;

        var normalizedOffset = Assert.IsType<DateTimeOffset>(MigrationValue.Normalize(offset));
        var normalizedDateTime = Assert.IsType<DateTime>(MigrationValue.Normalize(dateTime));

        Assert.Equal(offset.UtcTicks - 7, normalizedOffset.UtcTicks);
        Assert.Equal(dateTime.Ticks - 7, normalizedDateTime.Ticks);
        Assert.Equal(TimeSpan.Zero, normalizedOffset.Offset);
        Assert.Equal(DateTimeKind.Utc, normalizedDateTime.Kind);
    }
}
