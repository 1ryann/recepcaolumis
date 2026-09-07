using GestaoPredio.Domain.ProfessionalRegistrations;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalRegistrationRequestConfiguration : IEntityTypeConfiguration<ProfessionalRegistrationRequest>
{
    public void Configure(EntityTypeBuilder<ProfessionalRegistrationRequest> entity)
    {
        entity.ToTable("ProfessionalRegistrationRequests", table => table.HasCheckConstraint(
            "CK_ProfessionalRegistrationRequests_Status", "\"Status\" IN ('PENDING','APPROVED','REJECTED')"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ApplicationUserId).HasMaxLength(450).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Profession).HasMaxLength(150).IsRequired();
        entity.Property(x => x.WhatsApp).HasMaxLength(16).IsUnicode(false).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(500);
        entity.Property(x => x.Status).HasConversion(x => x == ProfessionalRegistrationStatus.Pending ? "PENDING" : x == ProfessionalRegistrationStatus.Approved ? "APPROVED" : "REJECTED", x => x == "PENDING" ? ProfessionalRegistrationStatus.Pending : x == "APPROVED" ? ProfessionalRegistrationStatus.Approved : ProfessionalRegistrationStatus.Rejected).HasMaxLength(16);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ReviewedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ReviewedByUserId).HasMaxLength(450);
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.ApplicationUserId).IsUnique();
        entity.HasIndex(x => new { x.Status, x.CreatedAt });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
