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
        var connection = ModulesApiFactory.CreateTestConnection("LumisDev");

        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Production", connection));
    }

    [Theory]
    [InlineData("GestaoPredioDB")]
    [InlineData("GestaoPredioHomolog")]
    [InlineData("OtherDatabase")]
    public void Factory_rejects_every_non_development_database(string database)
    {
        var connection = ModulesApiFactory.CreateTestConnection(database);

        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Testing", connection));
    }

    [Fact]
    public void Factory_accepts_only_local_LumisDev()
    {
        var connection = ModulesApiFactory.CreateTestConnection("LumisDev");

        ModulesApiFactory.ValidateTestConfiguration("Testing", connection);
    }

    [Fact]
    public void Factory_rejects_remote_PostgreSQL_host()
    {
        const string connection = "Host=db.example.test;Database=LumisDev;Username=postgres;Password=not-used";

        Assert.Throws<InvalidOperationException>(() =>
            ModulesApiFactory.ValidateTestConfiguration("Testing", connection));
    }

    [Fact]
    public void Factories_share_only_LumisDev_but_isolate_schema_and_private_storage()
    {
        using var first = new ModulesApiFactory();
        using var second = new ModulesApiFactory();

        Assert.Equal("LumisDev", first.DatabaseName);
        Assert.Equal(first.DatabaseName, second.DatabaseName);
        Assert.NotEqual(first.SchemaName, second.SchemaName);
        Assert.NotEqual(first.PrivateFilesRoot, second.PrivateFilesRoot);
        Assert.StartsWith("lumis_test_", first.SchemaName, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetTempPath(), first.PrivateFilesRoot, StringComparison.OrdinalIgnoreCase);
    }
}
