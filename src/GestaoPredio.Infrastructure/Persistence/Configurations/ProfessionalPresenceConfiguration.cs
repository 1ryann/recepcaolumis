using GestaoPredio.Domain.Professionals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalPresenceConfiguration : IEntityTypeConfiguration<ProfessionalPresence>
{
    public void Configure(EntityTypeBuilder<ProfessionalPresence> entity)
    {
        entity.ToTable("ProfessionalPresence");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndReason).HasColumnType("smallint");
        entity.Property(x => x.Source).HasColumnType("smallint");
        entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => new { x.ProfessionalId, x.StartedAt })
            .HasDatabaseName("IX_ProfessionalPresence_Professional_Started");
        // The single-open-presence invariant is a partial unique index added by the migration:
        //   CREATE UNIQUE INDEX "UX_ProfessionalPresence_Open"
        //     ON "ProfessionalPresence" ("ProfessionalId") WHERE "EndedAt" IS NULL;
        entity.HasIndex(x => x.ProfessionalId)
            .HasDatabaseName("UX_ProfessionalPresence_Open")
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
    }
}
