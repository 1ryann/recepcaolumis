namespace GestaoPredio.Domain.Auditing;

public sealed class AuditEntry
{
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

  if (fields.Any(fieldName => !AuditFields.All.Contains(fieldName, StringComparer.Ordinal)))
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

 public static readonly IReadOnlyList<string> All =
 [
  Name, Profession, WhatsApp, Description, HourlyRate, DailyRate,
  IsActive, PhotoFileId, ApplicationUserId
 ];
}
