using GestaoPredio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
namespace GestaoPredio.Infrastructure.Persistence;
public sealed class EfDatabaseProbe(ApplicationDbContext db) : IDatabaseProbe {
 public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => db.Database.CanConnectAsync(cancellationToken);
}
