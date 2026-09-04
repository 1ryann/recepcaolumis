using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Infrastructure.Persistence;
namespace GestaoPredio.Infrastructure.Auditing;
public sealed class EfAuditWriter(ApplicationDbContext db) : IAuditWriter {
 public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken) {
  db.AuditEntries.Add(entry);
  await db.SaveChangesAsync(cancellationToken);
 }
}
