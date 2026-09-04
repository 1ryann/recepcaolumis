using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace GestaoPredio.Infrastructure.Persistence;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
 public ApplicationDbContext CreateDbContext(string[] args) {
  // Generation requires a provider, not a connection. Deployment must pass --connection explicitly.
  return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer().Options);
 }
}
