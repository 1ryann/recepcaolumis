using GestaoPredio.Application.Finance;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Domain.Leases;

namespace GestaoPredio.UnitTests;

public sealed class FinancialChargeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo PortoVelho = OperationalTimeZone.Resolve("America/Porto_Velho");

    [Fact]
    public void Monthly_uses_billing_start_and_freezes_contracted_rate()
    {
        var lease = Lease.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Monthly, 50m,
            new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero), 31,
            new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero), null, 31, Now);
        var charges = new FinancialChargeCalculator(PortoVelho).Calculate(lease,
            new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, charges.Count);
        Assert.All(charges, item => Assert.Equal(50m, item.Amount));
        Assert.Equal(new DateOnly(2026, 2, 28), charges[0].DueDate);
    }

    [Fact]
    public void Daily_uses_civil_days_in_operational_timezone()
    {
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(2);
        var lease = Lease.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Daily, 100m,
            start, null, start, end, null, Now);

        var charges = new FinancialChargeCalculator(PortoVelho).Calculate(lease, end.AddMinutes(1));

        Assert.Equal(2, charges.Count);
        Assert.All(charges, item => Assert.Equal(100m, item.Amount));
    }

    [Fact]
    public void Hourly_uses_exact_minutes_without_float_rounding()
    {
        var start = Now;
        var end = start.AddMinutes(90);
        var lease = Lease.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Hourly, 50m,
            start, null, start, end, null, Now);

        var charge = Assert.Single(new FinancialChargeCalculator(PortoVelho).Calculate(lease, end));

        Assert.Equal(75m, charge.Amount);
        Assert.Contains("\"minutes\":90", charge.CalculationDetails);
    }

    [Fact]
    public void Financial_charge_preserves_calculated_amount_when_adjusted()
    {
        var charge = FinancialCharge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Now, Now.AddDays(1), DateOnly.FromDateTime(Now.DateTime), 100m, "{\"mode\":\"DAILY\"}", Now);

        charge.Adjust(80m, "Concessão aprovada", Now.AddMinutes(1));

        Assert.Equal(100m, charge.CalculatedAmount);
        Assert.Equal(80m, charge.FinalAmount);
    }

    [Fact]
    public void Overdue_is_derived_without_mutating_status()
    {
        var charge = FinancialCharge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Now, Now.AddDays(1), DateOnly.FromDateTime(Now.DateTime.AddDays(-1)), 100m, "{}", Now);

        Assert.True(charge.IsOverdue(DateOnly.FromDateTime(Now.DateTime)));
        Assert.Equal(FinancialChargeStatus.Pending, charge.Status);
    }
}
