using GestaoPredio.Domain.Security;

namespace GestaoPredio.UnitTests;

public sealed class CustomerAuthorizationTests
{
    [Fact]
    public void Customer_role_is_separate_from_administrative_roles()
    {
        Assert.Equal("CUSTOMER", SystemRoles.Customer);
        Assert.Contains(SystemRoles.Customer, SystemRoles.AuthenticationRoles);
        Assert.DoesNotContain(SystemRoles.Customer, SystemRoles.All);
        Assert.Equal(3, SystemRoles.All.Count);
    }
}
