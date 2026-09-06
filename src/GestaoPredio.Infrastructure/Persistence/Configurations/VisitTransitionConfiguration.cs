using GestaoPredio.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class VisitTransitionConfiguration : IEntityTypeConfiguration<VisitTransition>
{
    public void Configure(EntityTypeBuilder<VisitTransition> entity)
    {
        entity.ToTable("VisitTransitions", table =>
        {
            table.HasCheckConstraint("CK_VisitTransitions_PreviousStatus",
                "\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
            table.HasCheckConstraint("CK_VisitTransitions_NewStatus",
                "\"NewStatus\" IN ('WAITING', 'IN_SERVICE', 'ENDED', 'CANCELLED')");
            table.HasCheckConstraint("CK_VisitTransitions_CorrectionReason",
                "NOT \"IsCorrection\" OR \"Reason\" IS NOT NULL");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PreviousStatus).HasConversion(VisitConfiguration.StatusConverter()).HasMaxLength(20);
        entity.Property(x => x.NewStatus).HasConversion(VisitConfiguration.StatusConverter()).HasMaxLength(20).IsRequired();
        entity.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
        entity.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Reason).HasMaxLength(500);
        entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.VisitId, x.OccurredAt })
            .HasDatabaseName("IX_VisitTransitions_Visit_OccurredAt");
    }
}
