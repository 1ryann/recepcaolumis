namespace GestaoPredio.Domain.Auditing;
public sealed class AuditEntry {
 public Guid Id { get; set; }
 public string? ActorUserId { get; set; }
 public required string Action { get; set; }
 public required string Result { get; set; }
 public DateTimeOffset OccurredAt { get; set; }
 public required string CorrelationId { get; set; }
}
