using GestaoPredio.Domain.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class AccessDeviceConfiguration : IEntityTypeConfiguration<AccessDevice>
{
    public void Configure(EntityTypeBuilder<AccessDevice> entity)
    {
        entity.ToTable("AccessDevices");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SerialNumber).HasMaxLength(64).IsRequired();
        entity.Property(x => x.SecretHash).HasColumnType("bytea").IsRequired();
        entity.Property(x => x.LastSeenAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.Version).IsRowVersion();
        // The device authenticates by the secret in its configured path, so the lookup is by hash.
        entity.HasIndex(x => x.SecretHash).IsUnique().HasDatabaseName("UX_AccessDevices_SecretHash");
        entity.HasIndex(x => x.SerialNumber).IsUnique().HasDatabaseName("UX_AccessDevices_SerialNumber");
    }
}

public sealed class AccessEventConfiguration : IEntityTypeConfiguration<AccessEvent>
{
    public void Configure(EntityTypeBuilder<AccessEvent> entity)
    {
        entity.ToTable("AccessEvents", table => table.HasCheckConstraint("CK_AccessEvents_Disposition",
            "\"Disposition\" IN ('CAPTURED', 'DUPLICATE', 'REJECTED')"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Disposition).HasConversion(DispositionConverter()).HasMaxLength(20).IsRequired();
        entity.Property(x => x.PayloadShape).HasMaxLength(4000);
        entity.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
        // The guarantee that a replayed event is recorded once, not the hopeful read-then-write above it.
        entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_AccessEvents_IdempotencyKey");
        entity.HasIndex(x => new { x.DeviceId, x.ReceivedAt }).HasDatabaseName("IX_AccessEvents_Device_ReceivedAt");
        entity.HasOne<AccessDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.NoAction);
    }

    internal static ValueConverter<AccessEventDisposition, string> DispositionConverter() => new(
        value => value == AccessEventDisposition.Captured ? "CAPTURED"
            : value == AccessEventDisposition.Duplicate ? "DUPLICATE" : "REJECTED",
        value => value == "CAPTURED" ? AccessEventDisposition.Captured
            : value == "DUPLICATE" ? AccessEventDisposition.Duplicate : AccessEventDisposition.Rejected);
}
