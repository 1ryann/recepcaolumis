using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace GestaoPredio.Infrastructure.Persistence;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
 public ApplicationDbContext CreateDbContext(string[] args) {
  var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
  var options = new DbContextOptionsBuilder<ApplicationDbContext>();
  if (string.IsNullOrWhiteSpace(connection)) options.UseNpgsql();
  else options.UseNpgsql(connection);
  return new ApplicationDbContext(options.Options);
 }
}
