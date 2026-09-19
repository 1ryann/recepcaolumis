using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> entity)
    {
        entity.ToTable("Professionals", table =>
            table.HasCheckConstraint("CK_Professionals_AvailabilityMode", "\"AvailabilityMode\" BETWEEN 0 AND 1"));
        entity.MapWhatsAppOptIn("Professionals");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName).HasMaxLength(400).IsRequired();
        entity.Property(x => x.Profession).HasMaxLength(150).IsRequired();
        entity.Property(x => x.NormalizedProfession).HasMaxLength(300).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(500);
        entity.Property(x => x.WhatsApp).HasMaxLength(16).IsUnicode(false).IsRequired();
        entity.Property(x => x.ApplicationUserId).HasMaxLength(450);
        entity.Property(x => x.AvailabilityMode).HasColumnType("smallint").HasConversion<short>()
            .HasDefaultValue(ProfessionalAvailabilityMode.InheritGlobal).IsRequired();
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.ApplicationUserId).IsUnique()
            .HasDatabaseName("UX_Professionals_ApplicationUserId");
        entity.HasIndex(x => x.PhotoFileId).IsUnique()
            .HasDatabaseName("UX_Professionals_PhotoFileId");
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<PrivateFile>().WithMany().HasForeignKey(x => x.PhotoFileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
