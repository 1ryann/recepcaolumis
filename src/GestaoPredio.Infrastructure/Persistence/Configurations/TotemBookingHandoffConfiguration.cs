using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class TotemBookingHandoffConfiguration : IEntityTypeConfiguration<TotemBookingHandoff>
{
    public void Configure(EntityTypeBuilder<TotemBookingHandoff> entity)
    {
        entity.ToTable("TotemBookingHandoffs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.HandoffTokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.StatusTokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.Status).HasConversion<short>();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.HandoffTokenHash).IsUnique().HasDatabaseName("UX_TotemBookingHandoffs_HandoffTokenHash");
        entity.HasIndex(x => x.StatusTokenHash).IsUnique().HasDatabaseName("UX_TotemBookingHandoffs_StatusTokenHash");
        entity.HasIndex(x => x.ProfessionalId).HasDatabaseName("IX_TotemBookingHandoffs_ProfessionalId");
        entity.HasIndex(x => x.ReservationId).HasDatabaseName("IX_TotemBookingHandoffs_ReservationId");
        entity.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("IX_TotemBookingHandoffs_Status_ExpiresAt");
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.NoAction);
    }
}
