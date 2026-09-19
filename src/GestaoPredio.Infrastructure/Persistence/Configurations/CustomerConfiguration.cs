using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> entity)
    {
        entity.ToTable("Customers");
        entity.MapWhatsAppOptIn("Customers");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(Customer.MaximumNameLength).IsRequired();
        entity.Property(x => x.Phone).HasMaxLength(Customer.MaximumPhoneLength).IsRequired();
        entity.Property(x => x.NormalizedPhone).HasMaxLength(Customer.MaximumPhoneLength).IsRequired();
        entity.Property(x => x.ApplicationUserId).HasMaxLength(450);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.NormalizedPhone).IsUnique().HasDatabaseName("UX_Customers_NormalizedPhone");
        entity.HasIndex(x => x.ApplicationUserId).IsUnique().HasFilter("\"ApplicationUserId\" IS NOT NULL").HasDatabaseName("UX_Customers_ApplicationUserId");
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
