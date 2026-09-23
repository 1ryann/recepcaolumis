using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;

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
    // Reservations, visits and WhatsApp notices point at the customer, so a customer with history
    // cannot be deleted outright. Anonymising keeps those rows readable while the person is gone.
    [Fact]
    public void Anonymize_clears_the_person_and_keeps_the_record_addressable()
    {
        var customer = Customer.Create("Carlos Oliveira", "(69) 99999-9999", DateTimeOffset.UtcNow);
        customer.LinkUser("user-1", DateTimeOffset.UtcNow);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, DateTimeOffset.UtcNow);

        customer.Anonymize(DateTimeOffset.UtcNow);

        Assert.Equal("Cliente excluído", customer.Name);
        Assert.DoesNotContain("9999", customer.Phone);
        Assert.Equal(customer.Phone, customer.NormalizedPhone);
        Assert.True(customer.Phone.Length <= Customer.MaximumPhoneLength);
        Assert.Null(customer.ApplicationUserId);
        Assert.False(customer.IsActive);
        Assert.Equal(WhatsAppOptInStatus.Revoked, customer.WhatsAppOptInStatus);
    }

    // The phone column is unique, so two anonymised customers must not collide.
    [Fact]
    public void Anonymize_derives_a_distinct_phone_per_customer()
    {
        var first = Customer.Create("Ana", "(69) 99999-1111", DateTimeOffset.UtcNow);
        var second = Customer.Create("Bia", "(69) 99999-2222", DateTimeOffset.UtcNow);

        first.Anonymize(DateTimeOffset.UtcNow);
        second.Anonymize(DateTimeOffset.UtcNow);

        Assert.NotEqual(first.NormalizedPhone, second.NormalizedPhone);
    }
}