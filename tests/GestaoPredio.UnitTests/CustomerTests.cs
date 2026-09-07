using GestaoPredio.Domain.Customers;

namespace GestaoPredio.UnitTests;

public sealed class CustomerTests
{
    [Fact]
    public void Create_normalizes_name_and_phone_without_account()
    {
        var customer = Customer.Create("  Carlos   Oliveira ", "(69) 99999-9999", DateTimeOffset.UtcNow);

        Assert.Equal("Carlos Oliveira", customer.Name);
        Assert.Equal("+5569999999999", customer.Phone);
        Assert.Equal(customer.Phone, customer.NormalizedPhone);
        Assert.Null(customer.ApplicationUserId);
        Assert.True(customer.IsActive);
    }

    [Fact]
    public void Create_with_account_and_deactivate_is_supported()
    {
        var customer = Customer.Create("Ana", "+55 69 99999-9999", DateTimeOffset.UtcNow);
        customer.LinkUser("user-1", DateTimeOffset.UtcNow);
        customer.Deactivate(DateTimeOffset.UtcNow);

        Assert.Equal("user-1", customer.ApplicationUserId);
        Assert.False(customer.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("123")]
    public void Create_rejects_invalid_phone(string phone)
    {
        Assert.ThrowsAny<ArgumentException>(() => Customer.Create("Ana", phone, DateTimeOffset.UtcNow));
    }
}
