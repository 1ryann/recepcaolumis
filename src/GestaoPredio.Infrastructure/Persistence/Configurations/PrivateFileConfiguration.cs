using GestaoPredio.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class PrivateFileConfiguration : IEntityTypeConfiguration<PrivateFile>
{
    public void Configure(EntityTypeBuilder<PrivateFile> entity)
    {
        entity.ToTable("PrivateFiles", table =>
        {
            table.HasCheckConstraint("CK_PrivateFiles_Purpose", "\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')");
            table.HasCheckConstraint("CK_PrivateFiles_Length_Positive", "\"Length\" > 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StorageKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.MimeType).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.Purpose).HasMaxLength(50).IsUnicode(false).IsRequired();
        entity.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("UX_PrivateFiles_StorageKey");
    }
}
