using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class CheckInTokenConfiguration : IEntityTypeConfiguration<CheckInToken>
{
    public void Configure(EntityTypeBuilder<CheckInToken> entity)
    {
        entity.ToTable("CheckInTokens");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.IssuedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UsedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasIndex(x => x.ReservationId).IsUnique().HasDatabaseName("UX_CheckInTokens_ReservationId");
        entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_CheckInTokens_TokenHash");
        entity.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.NoAction);
    }
}
