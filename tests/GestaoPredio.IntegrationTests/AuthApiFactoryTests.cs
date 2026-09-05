namespace GestaoPredio.IntegrationTests;

public sealed class AuthApiFactoryTests
{
    [Theory]
    [InlineData("Server=.;Database=GestaoPredioDB;Integrated Security=true")]
    [InlineData("Server=.;Initial Catalog=OtherDatabase;Integrated Security=true")]
    public void Factory_rejects_non_test_database(string connection)
    {
        Assert.Throws<InvalidOperationException>(() => AuthApiFactory.ValidateTestConnection(connection));
    }

    [Fact]
    public void Factory_accepts_dedicated_auth_test_database()
    {
        AuthApiFactory.ValidateTestConnection(AuthApiFactory.TestConnection);
    }
}
