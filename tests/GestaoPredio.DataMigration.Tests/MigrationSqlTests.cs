using GestaoPredio.DataMigration;
using NpgsqlTypes;

namespace GestaoPredio.DataMigration.Tests;

public sealed class MigrationSqlTests
{
    [Fact]
    public void Copy_sql_uses_only_manifest_identifiers_and_parameterized_values()
    {
        var room = MigrationManifest.Tables.Single(x => x.Name == "Rooms");

        var source = MigrationSql.SourceSelect(room);
        var insert = MigrationSql.TargetInsert(room);

        Assert.Contains("[dbo].[Rooms]", source);
        Assert.DoesNotContain("RowVersion", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Rooms\"", insert);
        Assert.Contains("@p0", insert);
        Assert.DoesNotContain("VALUES ('", insert, StringComparison.Ordinal);
    }

    [Fact]
    public void Digest_compares_content_without_returning_the_content()
    {
        var rows = new object?[][]
        {
            [Guid.Parse("43d5f860-e23d-4d9f-9ce2-999997777777"), "sensitive", 100.10m,
                new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero)]
        };

        var digest = MigrationDigest.Compute(rows);

        Assert.Matches("^[A-F0-9]{64}$", digest);
        Assert.DoesNotContain("sensitive", digest);
        Assert.Equal(digest, MigrationDigest.Compute(rows));
        Assert.NotEqual(digest, MigrationDigest.Compute([[rows[0][0], "changed", rows[0][2], rows[0][3]]]));
    }

    [Fact]
    public void Digest_treats_sql_server_datetimeoffset_and_postgresql_utc_datetime_as_equal()
    {
        var instant = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            MigrationDigest.Compute([[instant]]),
            MigrationDigest.Compute([[instant.UtcDateTime]]));
    }

    [Fact]
    public void Null_target_parameter_is_sent_as_postgresql_unknown_for_server_side_inference()
    {
        var parameter = MigrationParameter.Create("p0", null);

        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(NpgsqlDbType.Unknown, parameter.NpgsqlDbType);
    }
}
