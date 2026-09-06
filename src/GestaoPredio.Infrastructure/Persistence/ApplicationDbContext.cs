using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace GestaoPredio.Infrastructure.Persistence;
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options) {
 public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
 public DbSet<Professional> Professionals => Set<Professional>();
 public DbSet<Room> Rooms => Set<Room>();
 public DbSet<PrivateFile> PrivateFiles => Set<PrivateFile>();
 public DbSet<Tenant> Tenants => Set<Tenant>();
 public DbSet<Lease> Leases => Set<Lease>();
 public DbSet<LeaseOccurrence> LeaseOccurrences => Set<LeaseOccurrence>();
 public DbSet<Reservation> Reservations => Set<Reservation>();
 protected override void OnModelCreating(ModelBuilder builder) {
  base.OnModelCreating(builder);
  builder.HasPostgresExtension("extensions", "unaccent");
  builder.HasDbFunction(typeof(PostgreSqlText).GetMethod(nameof(PostgreSqlText.Unaccent))!)
   .HasName("unaccent")
   .HasSchema("extensions");
  builder.ApplyConfiguration(new ProfessionalConfiguration());
  builder.ApplyConfiguration(new RoomConfiguration());
  builder.ApplyConfiguration(new PrivateFileConfiguration());
  builder.ApplyConfiguration(new TenantConfiguration());
  builder.ApplyConfiguration(new LeaseConfiguration());
  builder.ApplyConfiguration(new LeaseOccurrenceConfiguration());
  builder.ApplyConfiguration(new ReservationConfiguration());
  // Preserve the existing Identity schema and composite key sizes.
  builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
  builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
  builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
  builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);
  builder.Entity<ApplicationUser>(entity => {
   entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired().HasDefaultValue("");
   entity.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
   entity.Property(x => x.MustChangePassword).IsRequired().HasDefaultValue(false);
  });
  builder.Entity<AuditEntry>(entity => {
   entity.ToTable("AuditEntries");
   entity.HasKey(x => x.Id);
   entity.Property(x => x.ActorUserId).HasMaxLength(450);
   entity.Property(x => x.TargetUserId).HasMaxLength(450);
   entity.Property(x => x.IpAddress).HasMaxLength(45);
   entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
   entity.Property(x => x.Result).HasMaxLength(50).IsRequired();
   entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
   entity.Property(x => x.TargetEntityType).HasMaxLength(50);
   entity.Property(x => x.ChangedFields).HasMaxLength(500);
   entity.HasIndex(x => new { x.TargetEntityType, x.TargetEntityId, x.OccurredAt })
    .HasDatabaseName("IX_AuditEntries_TargetEntity");
   entity.HasIndex(x => x.OccurredAt);
   entity.HasIndex(x => new { x.Action, x.OccurredAt });
  });
 }
}
