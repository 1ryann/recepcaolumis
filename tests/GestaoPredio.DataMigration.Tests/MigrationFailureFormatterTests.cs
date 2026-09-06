using GestaoPredio.DataMigration;

namespace GestaoPredio.DataMigration.Tests;

public sealed class MigrationFailureFormatterTests
{
    [Fact]
    public void Generic_failure_does_not_include_exception_message_or_secret_values()
    {
        var exception = new InvalidOperationException(
            "Password=do-not-log; row=personal-data");

        var output = MigrationFailureFormatter.Format(exception);

        Assert.Contains(nameof(InvalidOperationException), output, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-log", output, StringComparison.Ordinal);
        Assert.DoesNotContain("personal-data", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The target principal name is incorrect. Password=secret", "WINDOWS_AUTH")]
    [InlineData("The certificate chain was issued by an authority that is not trusted", "TLS")]
    [InlineData("A network-related or instance-specific error occurred", "NETWORK")]
    [InlineData("Cannot open database requested by the login", "DATABASE")]
    public void Sql_failure_category_is_closed_and_does_not_echo_the_message(string message, string expected)
    {
        var category = MigrationFailureFormatter.ClassifySqlFailure(message);

        Assert.Equal(expected, category);
        Assert.DoesNotContain("secret", category, StringComparison.OrdinalIgnoreCase);
    }
}
