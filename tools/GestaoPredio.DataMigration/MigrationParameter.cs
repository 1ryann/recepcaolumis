using Npgsql;
using NpgsqlTypes;

namespace GestaoPredio.DataMigration;

public static class MigrationParameter
{
    public static NpgsqlParameter Create(string name, object? value)
    {
        if (value is null or DBNull)
            return new NpgsqlParameter(name, NpgsqlDbType.Unknown) { Value = DBNull.Value };
        return new NpgsqlParameter(name, MigrationValue.Normalize(value));
    }
}
