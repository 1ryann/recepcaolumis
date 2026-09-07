using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.IntegrationTests;

public sealed class CustomerModelTests
{
    [Fact]
    public void Customer_model_has_private_phone_and_account_uniqueness()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var entity = db.Model.FindEntityType(typeof(Customer))!;
        Assert.Equal("Customers", entity.GetTableName());
        Assert.Equal(16, entity.FindProperty(nameof(Customer.NormalizedPhone))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(Customer.NormalizedPhone));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(Customer.ApplicationUserId));
        Assert.Contains(entity.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(Customer.ApplicationUserId) && fk.DeleteBehavior == DeleteBehavior.NoAction);
    }
}
