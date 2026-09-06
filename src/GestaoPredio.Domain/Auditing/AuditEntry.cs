using System.Collections.Frozen;

namespace GestaoPredio.Domain.Auditing;

public sealed class AuditEntry
{
 private static readonly FrozenSet<string> ApprovedChangedFields =
 new[]
 {
  AuditFields.Name, AuditFields.Profession, AuditFields.WhatsApp,
  AuditFields.Description, AuditFields.HourlyRate, AuditFields.DailyRate,
  AuditFields.IsActive, AuditFields.PhotoFileId, AuditFields.ApplicationUserId,
  AuditFields.TenantId, AuditFields.ProfessionalId, AuditFields.RoomId, AuditFields.Mode,
  AuditFields.ContractedRate, AuditFields.BillingStartAt, AuditFields.BillingDueDay,
  AuditFields.OccupancyStartAt, AuditFields.OccupancyEndAt
 }.ToFrozenSet(StringComparer.Ordinal);

 public Guid Id { get; set; }
 public string? ActorUserId { get; set; }
 public string? TargetUserId { get; set; }
 public string? IpAddress { get; set; }
 public required string Action { get; set; }
 public required string Result { get; set; }
 public DateTimeOffset OccurredAt { get; set; }
 public required string CorrelationId { get; set; }
 public string? TargetEntityType { get; set; }
 public Guid? TargetEntityId { get; set; }
 public string? ChangedFields { get; private set; }

 public void SetChangedFields(IEnumerable<string>? fieldNames)
 {
  ChangedFields = fieldNames is null ? null : BuildChangedFields(fieldNames);
 }

 private static string BuildChangedFields(IEnumerable<string> fieldNames)
 {
  ArgumentNullException.ThrowIfNull(fieldNames);

  var fields = fieldNames
   .Distinct(StringComparer.Ordinal)
   .OrderBy(fieldName => fieldName, StringComparer.Ordinal)
   .ToArray();

  if (fields.Any(fieldName => !ApprovedChangedFields.Contains(fieldName)))
   throw new ArgumentException("A auditoria aceita apenas nomes de campos aprovados.", nameof(fieldNames));

  var changedFields = string.Join(',', fields);
  if (changedFields.Length > 500)
   throw new ArgumentOutOfRangeException(nameof(fieldNames), "Os campos alterados excedem o limite persistido.");

  return changedFields;
 }
}

public static class AuditFields
{
 public const string Name = "Name";
 public const string Profession = "Profession";
 public const string WhatsApp = "WhatsApp";
 public const string Description = "Description";
 public const string HourlyRate = "HourlyRate";
 public const string DailyRate = "DailyRate";
 public const string IsActive = "IsActive";
 public const string PhotoFileId = "PhotoFileId";
 public const string ApplicationUserId = "ApplicationUserId";
 public const string TenantId = "TenantId";
 public const string ProfessionalId = "ProfessionalId";
 public const string RoomId = "RoomId";
 public const string Mode = "Mode";
 public const string ContractedRate = "ContractedRate";
 public const string BillingStartAt = "BillingStartAt";
 public const string BillingDueDay = "BillingDueDay";
 public const string OccupancyStartAt = "OccupancyStartAt";
 public const string OccupancyEndAt = "OccupancyEndAt";

}
