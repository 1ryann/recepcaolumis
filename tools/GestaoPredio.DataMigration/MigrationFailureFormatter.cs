using Microsoft.Data.SqlClient;
using System.ComponentModel;

namespace GestaoPredio.DataMigration;

public static class MigrationFailureFormatter
{
    public static string Format(Exception exception)
    {
        var detail = exception is SqlException sqlException
            ? FormatSqlException(sqlException)
            : exception.GetType().Name;

        return $"Migration failed safely ({detail}). No connection values or row data were logged.";
    }

    public static string ClassifySqlFailure(string message)
    {
        if (ContainsAny(message, "target principal name", "cannot generate sspi", "sspi context", "untrusted domain"))
            return "WINDOWS_AUTH";
        if (ContainsAny(message, "certificate", "ssl provider", "tls"))
            return "TLS";
        if (ContainsAny(message, "cannot open database", "database requested by the login"))
            return "DATABASE";
        if (ContainsAny(message, "login failed", "authentication"))
            return "AUTHENTICATION";
        if (ContainsAny(message, "network-related", "instance-specific", "server was not found", "actively refused", "error: 40", "tcp provider"))
            return "NETWORK";
        return "UNKNOWN";
    }

    private static string CollectMessages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        if (exception is SqlException sqlException)
            messages.AddRange(sqlException.Errors.Cast<SqlError>().Select(error => error.Message));
        return string.Join(" ", messages);
    }

    private static bool ContainsAny(string input, params string[] terms) =>
        terms.Any(term => input.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FormatSqlException(SqlException exception)
    {
        var inner = exception.InnerException;
        var innerCode = inner is Win32Exception win32 ? win32.NativeErrorCode : inner?.HResult;
        var innerDetail = inner is null
            ? "None"
            : $"{inner.GetType().Name}:{innerCode}";
        return $"SqlException; Category={ClassifySqlFailure(CollectMessages(exception))}; Number={exception.Number}; State={exception.State}; Class={exception.Class}; Inner={innerDetail}";
    }
}
