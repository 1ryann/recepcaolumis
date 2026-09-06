using GestaoPredio.Domain.Tenants;

namespace GestaoPredio.UnitTests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 16, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Create_starts_active_and_normalizes_the_name_with_the_shared_rule()
    {
        var tenant = Tenant.Create("  Clínica  São José  ", TenantKind.LegalEntity, Now);

        Assert.NotEqual(Guid.Empty, tenant.Id);
        Assert.Equal("  Clínica  São José  ", tenant.Name);
        Assert.Equal("CLINICA SAO JOSE", tenant.NormalizedName);
        Assert.Equal(TenantKind.LegalEntity, tenant.Kind);
        Assert.True(tenant.IsActive);
        Assert.Equal(Now.ToUniversalTime(), tenant.CreatedAt);
        Assert.Equal(Now.ToUniversalTime(), tenant.UpdatedAt);
    }

    [Fact]
    public void Homonyms_are_allowed_and_receive_different_ids()
    {
        var first = Tenant.Create("Ana", TenantKind.Individual, Now);
        var second = Tenant.Create("Ana", TenantKind.Individual, Now);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.NormalizedName, second.NormalizedName);
    }

    [Fact]
    public void Update_and_status_operations_change_only_the_approved_state()
    {
        var tenant = Tenant.Create("Ana", TenantKind.Individual, Now);

        tenant.Update("Empresa Ágil", TenantKind.LegalEntity, Now.AddMinutes(1));
        tenant.Deactivate(Now.AddMinutes(2));
        tenant.Activate(Now.AddMinutes(3));

        Assert.Equal("Empresa Ágil", tenant.Name);
        Assert.Equal("EMPRESA AGIL", tenant.NormalizedName);
        Assert.Equal(TenantKind.LegalEntity, tenant.Kind);
        Assert.True(tenant.IsActive);
        Assert.Equal(Now.AddMinutes(3).ToUniversalTime(), tenant.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string name)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create(name, TenantKind.Individual, Now));
    }

    [Fact]
    public void Create_rejects_a_name_above_the_input_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Tenant.Create(new string('a', Tenant.MaximumNameLength + 1), TenantKind.Individual, Now));
    }

    [Fact]
    public void Create_rejects_an_unknown_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Tenant.Create("Ana", (TenantKind)999, Now));
    }

    [Fact]
    public void Server_owned_properties_have_no_public_setter()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(Tenant.NormalizedName), nameof(Tenant.IsActive), nameof(Tenant.CreatedAt),
                     nameof(Tenant.UpdatedAt), nameof(Tenant.Version)
                 })
        {
            Assert.False(typeof(Tenant).GetProperty(propertyName)!.SetMethod?.IsPublic ?? false);
        }
    }
}
