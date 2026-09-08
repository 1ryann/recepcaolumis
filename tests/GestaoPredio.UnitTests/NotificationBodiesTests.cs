using GestaoPredio.Application.Notifications;

namespace GestaoPredio.UnitTests;

public sealed class NotificationBodiesTests
{
    [Fact]
    public void CustomerReschedule_carries_the_link_and_no_sensitive_data()
    {
        var body = NotificationBodies.CustomerReschedule("Ana Martins", "https://lumis.example/reagendar/abc");

        Assert.Contains("https://lumis.example/reagendar/abc", body);
        Assert.Contains("Ana", body);
        Assert.Contains("48 horas", body);
        Assert.DoesNotContain("motivo", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", body);
        Assert.DoesNotContain("+55", body);
    }

    [Fact]
    public void CustomerReschedule_falls_back_when_the_name_is_blank()
    {
        var body = NotificationBodies.CustomerReschedule("  ", "https://x/y");
        Assert.Contains("imprevisto do profissional", body);
    }
}
