using GestaoPredio.Domain.Professionals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalPresenceTokenConfiguration : IEntityTypeConfiguration<ProfessionalPresenceToken>
{
    public void Configure(EntityTypeBuilder<ProfessionalPresenceToken> entity)
    {
        entity.ToTable("ProfessionalPresenceTokens");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.IssuedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UsedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.ProfessionalId).IsUnique().HasDatabaseName("UX_ProfessionalPresenceTokens_ProfessionalId");
        entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_ProfessionalPresenceTokens_TokenHash");
        entity.HasOne<Professional>().WithMany().HasForeignKey(x => x.ProfessionalId).OnDelete(DeleteBehavior.NoAction);
    }
}
