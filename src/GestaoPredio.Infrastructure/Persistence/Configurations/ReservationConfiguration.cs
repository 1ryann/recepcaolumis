using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> entity)
    {
        entity.ToTable("Reservations", table =>
        {
            table.HasCheckConstraint("CK_Reservations_Kind", "\"Kind\" IN ('NEW', 'RESCHEDULE', 'CANCELLATION')");
            table.HasCheckConstraint("CK_Reservations_Status", "\"Status\" IN ('PENDING', 'APPROVED', 'REJECTED', 'CANCELLED')");
            table.HasCheckConstraint("CK_Reservations_Period", "\"EndAt\" > \"StartAt\"");
            table.HasCheckConstraint("CK_Reservations_RejectionReason", "\"Status\" <> 'REJECTED' OR \"RejectionReason\" IS NOT NULL");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Kind)
            .HasConversion(new ValueConverter<ReservationKind, string>(
                value => value == ReservationKind.New ? "NEW" : value == ReservationKind.Reschedule ? "RESCHEDULE" : "CANCELLATION",
                value => value == "NEW" ? ReservationKind.New : value == "RESCHEDULE" ? ReservationKind.Reschedule : ReservationKind.Cancellation))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion(new ValueConverter<ReservationStatus, string>(
                value => value == ReservationStatus.Pending ? "PENDING" : value == ReservationStatus.Approved ? "APPROVED" : value == ReservationStatus.Rejected ? "REJECTED" : "CANCELLED",
                value => value == "PENDING" ? ReservationStatus.Pending : value == "APPROVED" ? ReservationStatus.Approved : value == "REJECTED" ? ReservationStatus.Rejected : ReservationStatus.Cancelled))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.StartAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
        entity.Property(x => x.RequestedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.DecidedByUserId).HasMaxLength(450);
        entity.Property(x => x.DecidedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Reservation>().WithMany().HasForeignKey(x => x.OriginalReservationId).OnDelete(DeleteBehavior.NoAction);

        entity.HasIndex(x => new { x.RoomId, x.Status, x.StartAt }).HasDatabaseName("IX_Reservations_Room_Status_Start");
        entity.HasIndex(x => new { x.ProfessionalId, x.Status, x.StartAt }).HasDatabaseName("IX_Reservations_Professional_Status_Start");
        entity.HasIndex(x => x.OriginalReservationId).HasDatabaseName("IX_Reservations_OriginalReservationId");
    }
}
