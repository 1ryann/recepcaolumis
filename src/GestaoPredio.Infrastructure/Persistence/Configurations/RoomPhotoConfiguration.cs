using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class RoomPhotoConfiguration : IEntityTypeConfiguration<RoomPhoto>
{
    public void Configure(EntityTypeBuilder<RoomPhoto> entity)
    {
        entity.ToTable("RoomPhotos");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<PrivateFile>().WithMany().HasForeignKey(x => x.PrivateFileId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.RoomId, x.SortOrder }).HasDatabaseName("IX_RoomPhotos_Room_SortOrder");
        entity.HasIndex(x => x.RoomId).IsUnique().HasFilter("\"IsCover\"")
            .HasDatabaseName("UX_RoomPhotos_Room_Cover");
    }
}
