using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace GestaoPredio.Infrastructure.Persistence;
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options) {
 public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
 protected override void OnModelCreating(ModelBuilder builder) {
  base.OnModelCreating(builder);
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
   entity.HasIndex(x => x.OccurredAt);
   entity.HasIndex(x => new { x.Action, x.OccurredAt });
  });
 }
}
