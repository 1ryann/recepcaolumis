using GestaoPredio.Domain.Professionals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalAvailabilityExceptionConfiguration
    : IEntityTypeConfiguration<ProfessionalAvailabilityException>
{
    public void Configure(EntityTypeBuilder<ProfessionalAvailabilityException> entity)
    {
        entity.ToTable("ProfessionalAvailabilityExceptions", table =>
            table.HasCheckConstraint("CK_ProfessionalAvailabilityExceptions_Shape",
                "(\"AllDay\" = TRUE AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR " +
                "(\"AllDay\" = FALSE AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"EndTime\" > \"StartTime\")"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Date).HasColumnType("date").IsRequired();
        entity.Property(x => x.StartTime).HasColumnType("time without time zone");
        entity.Property(x => x.EndTime).HasColumnType("time without time zone");
        entity.Property(x => x.Reason).HasMaxLength(ProfessionalAvailabilityException.MaximumReasonLength);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.ProfessionalId, x.Date, x.AllDay, x.StartTime })
            .HasDatabaseName("IX_ProfessionalAvailabilityExceptions_Professional_Date_Start");
        entity.HasIndex(x => new { x.ProfessionalId, x.Date }).IsUnique()
            .HasFilter("\"AllDay\" = TRUE")
            .HasDatabaseName("UX_ProfessionalAvailabilityExceptions_Professional_Date_AllDay");
        entity.HasIndex(x => new { x.ProfessionalId, x.Date, x.StartTime }).IsUnique()
            .HasFilter("\"AllDay\" = FALSE")
            .HasDatabaseName("UX_ProfessionalAvailabilityExceptions_Professional_Date_Start");
    }
}
