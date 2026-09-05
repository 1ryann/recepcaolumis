namespace GestaoPredio.IntegrationTests;

public sealed class ModulesApiFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_rejects_missing_connection(string? connection)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Testing", connection));
    }

    [Fact]
    public void Factory_rejects_production_before_database_access()
    {
        var connection = ModulesApiFactory.CreateTestConnection("GestaoPredioModulesTests_Guard");

        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Production", connection));
    }

    [Theory]
    [InlineData("GestaoPredioDB")]
    [InlineData("GestaoPredioAuthTests")]
    [InlineData("OtherDatabase")]
    public void Factory_rejects_every_non_modules_database(string database)
    {
        var connection = ModulesApiFactory.CreateTestConnection(database);

        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Testing", connection));
    }

    [Theory]
    [InlineData("GestaoPredioModulesTests")]
    [InlineData("GestaoPredioModulesTests_Queries")]
    public void Factory_accepts_only_the_modules_test_prefix(string database)
    {
        var connection = ModulesApiFactory.CreateTestConnection(database);

        ModulesApiFactory.ValidateTestConfiguration("Testing", connection);
    }

    [Fact]
    public void Factories_do_not_share_database_or_private_storage()
    {
        using var first = new ModulesApiFactory();
        using var second = new ModulesApiFactory();

        Assert.NotEqual(first.DatabaseName, second.DatabaseName);
        Assert.NotEqual(first.ConnectionString, second.ConnectionString);
        Assert.NotEqual(first.PrivateFilesRoot, second.PrivateFilesRoot);
        Assert.StartsWith("GestaoPredioModulesTests_", first.DatabaseName, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetTempPath(), first.PrivateFilesRoot, StringComparison.OrdinalIgnoreCase);
    }
}
