using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppOptInTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_recipient_has_no_recorded_opt_in_and_nothing_is_assumed()
    {
        var customer = Customer.Create("Ana Souza", "+5569999990001", Now);
        var professional = Professional.Create("Dra. Ana", "Psicologia", "+5569999990002", Now);

        foreach (var optIn in new[] { customer.WhatsAppOptIn, professional.WhatsAppOptIn })
        {
            Assert.Equal(WhatsAppOptInStatus.NotRecorded, optIn.Status);
            Assert.False(optIn.IsGranted);
            Assert.Null(optIn.ChangedAt);
            Assert.Null(optIn.Source);
            Assert.Null(optIn.TextVersion);
        }
    }

    [Fact]
    public void Granting_records_who_collected_it_when_and_which_wording_the_person_saw()
    {
        var customer = Customer.Create("Ana Souza", "+5569999990001", Now);

        Assert.True(customer.GrantWhatsAppOptIn(WhatsAppOptInSource.Totem, Now.AddMinutes(1)));

        var optIn = customer.WhatsAppOptIn;
        Assert.True(optIn.IsGranted);
        Assert.Equal(WhatsAppOptInStatus.Granted, optIn.Status);
        Assert.Equal(WhatsAppOptInSource.Totem, optIn.Source);
        Assert.Equal(Now.AddMinutes(1), optIn.ChangedAt);
        Assert.Equal(WhatsAppOptInState.CurrentTextVersion, optIn.TextVersion);
        Assert.Equal(Now.AddMinutes(1), customer.UpdatedAt);
    }

    [Fact]
    public void Granting_again_with_the_same_wording_changes_nothing()
    {
        var customer = Customer.Create("Ana Souza", "+5569999990001", Now);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, Now);

        Assert.False(customer.GrantWhatsAppOptIn(WhatsAppOptInSource.Totem, Now.AddDays(1)));

        Assert.Equal(WhatsAppOptInSource.CustomerRegistration, customer.WhatsAppOptIn.Source);
        Assert.Equal(Now, customer.WhatsAppOptIn.ChangedAt);
    }

    [Fact]
    public void Revoking_stops_messages_and_a_new_explicit_grant_is_needed_to_resume()
    {
        var customer = Customer.Create("Ana Souza", "+5569999990001", Now);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, Now);

        Assert.True(customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.Reception, Now.AddHours(1)));
        Assert.False(customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, Now.AddHours(2)));

        Assert.Equal(WhatsAppOptInStatus.Revoked, customer.WhatsAppOptIn.Status);
        Assert.False(customer.WhatsAppOptIn.IsGranted);
        Assert.Equal(WhatsAppOptInSource.Reception, customer.WhatsAppOptIn.Source);
        Assert.Equal(Now.AddHours(1), customer.WhatsAppOptIn.ChangedAt);
        Assert.Null(customer.WhatsAppOptIn.TextVersion);

        Assert.True(customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerPortal, Now.AddHours(3)));
        Assert.True(customer.WhatsAppOptIn.IsGranted);
    }

    [Fact]
    public void An_objection_is_recorded_even_when_no_opt_in_existed()
    {
        var customer = Customer.Create("Ana Souza", "+5569999990001", Now);

        Assert.True(customer.RevokeWhatsAppOptIn(WhatsAppOptInSource.Reception, Now));

        Assert.Equal(WhatsAppOptInStatus.Revoked, customer.WhatsAppOptIn.Status);
    }

    [Fact]
    public void A_professional_opt_in_belongs_to_the_number_it_was_given_for()
    {
        var professional = Professional.Create("Dra. Ana", "Psicologia", "+5569999990002", Now);
        professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, Now);

        professional.Update("Dra. Ana", "Psicologia", "(69) 99999-0002", Now.AddHours(1));   // same number, other format
        Assert.True(professional.WhatsAppOptIn.IsGranted);

        professional.Update("Dra. Ana", "Psicologia", "+5569988880002", Now.AddHours(2));    // new number
        Assert.Equal(WhatsAppOptInStatus.NotRecorded, professional.WhatsAppOptIn.Status);
        Assert.False(professional.WhatsAppOptIn.IsGranted);
        Assert.Equal(Now.AddHours(2), professional.WhatsAppOptIn.ChangedAt);
        Assert.Null(professional.WhatsAppOptIn.Source);
        Assert.Null(professional.WhatsAppOptIn.TextVersion);
    }

    [Fact]
    public void A_professional_can_grant_and_revoke_like_a_customer()
    {
        var professional = Professional.Create("Dra. Ana", "Psicologia", "+5569999990002", Now);

        Assert.True(professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, Now));
        Assert.True(professional.WhatsAppOptIn.IsGranted);
        Assert.True(professional.RevokeWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, Now.AddMinutes(5)));
        Assert.Equal(WhatsAppOptInStatus.Revoked, professional.WhatsAppOptIn.Status);
    }
}
