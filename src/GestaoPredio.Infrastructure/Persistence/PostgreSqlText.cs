using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Persistence;

public static class PostgreSqlText
{
    [DbFunction("unaccent", "extensions")]
    public static string Unaccent(string value) =>
        throw new InvalidOperationException("This method is translated by EF Core and cannot be evaluated locally.");
}
