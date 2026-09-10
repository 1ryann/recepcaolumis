using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.IntegrationTests;

public sealed class TotemBookingHandoffModelTests
{
    [Fact]
    public void Handoff_table_hashes_are_unique_and_reservation_fk_is_no_action()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var e = db.Model.FindEntityType(typeof(TotemBookingHandoff))!;
        Assert.Equal("TotemBookingHandoffs", e.GetTableName());
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(TotemBookingHandoff.HandoffTokenHash));
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(TotemBookingHandoff.StatusTokenHash));
        Assert.Contains(e.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(TotemBookingHandoff.ProfessionalId) && fk.DeleteBehavior == DeleteBehavior.NoAction);
        Assert.Equal("bytea", e.FindProperty(nameof(TotemBookingHandoff.HandoffTokenHash))!.GetColumnType());
    }
}
