namespace GestaoPredio.IntegrationTests;

public sealed class AuthApiFactoryTests
{
    [Theory]
    [InlineData("Host=localhost;Database=GestaoPredioDB;Username=postgres;Password=not-used")]
    [InlineData("Host=localhost;Database=OtherDatabase;Username=postgres;Password=not-used")]
    [InlineData("Host=db.example.test;Database=LumisDev;Username=postgres;Password=not-used")]
    public void Factory_rejects_non_local_development_database(string connection)
    {
        Assert.Throws<InvalidOperationException>(() => AuthApiFactory.ValidateTestConnection(connection));
    }

    [Fact]
    public void Factory_accepts_local_LumisDev()
    {
        AuthApiFactory.ValidateTestConnection(
            "Host=127.0.0.1;Port=5432;Database=LumisDev;Username=postgres;Password=not-used");
    }
}
