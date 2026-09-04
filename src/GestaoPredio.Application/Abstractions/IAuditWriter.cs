using GestaoPredio.Domain.Auditing;
namespace GestaoPredio.Application.Abstractions;
public interface IAuditWriter { Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken); }
