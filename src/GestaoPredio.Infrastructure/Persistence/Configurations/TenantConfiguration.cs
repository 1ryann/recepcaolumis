using GestaoPredio.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> entity)
    {
        entity.ToTable("Tenants", table =>
            table.HasCheckConstraint("CK_Tenants_Kind", "\"Kind\" IN ('INDIVIDUAL', 'LEGAL_ENTITY')"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(Tenant.MaximumNameLength).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(Tenant.MaximumNormalizedNameLength).IsRequired();
        entity.Property(x => x.Kind)
            .HasConversion(new ValueConverter<TenantKind, string>(
                value => value == TenantKind.Individual ? "INDIVIDUAL" : "LEGAL_ENTITY",
                value => value == "INDIVIDUAL" ? TenantKind.Individual : TenantKind.LegalEntity))
            .HasMaxLength(20)
            .IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => new { x.NormalizedName, x.Id }).HasDatabaseName("IX_Tenants_NormalizedName");
    }
}
