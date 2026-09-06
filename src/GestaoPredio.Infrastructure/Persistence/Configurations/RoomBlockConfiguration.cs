using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class RoomBlockConfiguration : IEntityTypeConfiguration<RoomBlock>
{
    public void Configure(EntityTypeBuilder<RoomBlock> entity)
    {
        entity.ToTable("RoomBlocks", table =>
        {
            table.HasCheckConstraint("CK_RoomBlocks_Period", "\"EndAt\" > \"StartAt\"");
            table.HasCheckConstraint("CK_RoomBlocks_Status", "\"Status\" IN ('ACTIVE', 'CANCELLED')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StartAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.EndAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Reason).HasMaxLength(RoomBlock.MaximumReasonLength).IsRequired();
        entity.Property(x => x.Status).HasConversion(new ValueConverter<RoomBlockStatus, string>(
            value => value == RoomBlockStatus.Active ? "ACTIVE" : "CANCELLED",
            value => value == "ACTIVE" ? RoomBlockStatus.Active : RoomBlockStatus.Cancelled))
            .HasMaxLength(20).IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CancelledAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CancelledBy).HasMaxLength(450);
        entity.Property(x => x.Version).IsRowVersion();
        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.RoomId, x.Status, x.StartAt })
            .HasDatabaseName("IX_RoomBlocks_Room_Status_Start");
        entity.HasIndex(x => new { x.RoomId, x.EndAt })
            .HasDatabaseName("IX_RoomBlocks_Room_End");
    }
}
