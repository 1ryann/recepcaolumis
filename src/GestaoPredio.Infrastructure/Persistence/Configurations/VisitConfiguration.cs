using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> entity)
    {
        entity.ToTable("Visits", table =>
        {
            table.HasCheckConstraint("CK_Visits_Status",
                "\"Status\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
            table.HasCheckConstraint("CK_Visits_StateTimestamps", """
                ("Status" = 'WAITING' AND "ServiceStartedAt" IS NULL AND "EndedAt" IS NULL AND "CancelledAt" IS NULL)
                OR ("Status" = 'IN_SERVICE' AND "ServiceStartedAt" IS NOT NULL AND "EndedAt" IS NULL AND "CancelledAt" IS NULL)
                OR ("Status" = 'ENDED' AND "ServiceStartedAt" IS NOT NULL AND "EndedAt" IS NOT NULL AND "CancelledAt" IS NULL)
                OR ("Status" = 'CANCELLED' AND "EndedAt" IS NULL AND "CancelledAt" IS NOT NULL)
                """);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.VisitorName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Status).HasConversion(StatusConverter()).HasMaxLength(20).IsRequired();
        entity.Property(x => x.ArrivedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ServiceStartedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CancelledAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.Status, x.ArrivedAt }).HasDatabaseName("IX_Visits_Status_ArrivedAt");
        entity.HasIndex(x => new { x.ProfessionalId, x.Status, x.ArrivedAt })
            .HasDatabaseName("IX_Visits_Professional_Status_ArrivedAt");
        entity.HasIndex(x => new { x.RoomId, x.Status, x.ArrivedAt }).HasDatabaseName("IX_Visits_Room_Status_ArrivedAt");
        entity.HasIndex(x => x.ReservationId).HasDatabaseName("IX_Visits_ReservationId");
    }

    internal static ValueConverter<VisitStatus, string> StatusConverter() => new(
        value => value == VisitStatus.Waiting ? "WAITING" :
            value == VisitStatus.InService ? "IN_SERVICE" :
            value == VisitStatus.Ended ? "ENDED" : "CANCELLED",
        value => value == "WAITING" ? VisitStatus.Waiting :
            value == "IN_SERVICE" ? VisitStatus.InService :
            value == "ENDED" ? VisitStatus.Ended : VisitStatus.Cancelled);
}
