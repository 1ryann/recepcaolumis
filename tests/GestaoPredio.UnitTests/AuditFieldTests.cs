using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.UnitTests;

public sealed class AuditFieldTests
{
    [Fact]
    public void BuildChangedFields_sorts_and_deduplicates_approved_field_names()
    {
        var changedFields = AuditEntry.BuildChangedFields([
            AuditFields.WhatsApp,
            AuditFields.Name,
            AuditFields.Profession,
            AuditFields.Name
        ]);

        Assert.Equal("Name,Profession,WhatsApp", changedFields);
    }

    [Theory]
    [InlineData("WhatsApp:+5565999991234")]
    [InlineData("Name=ana@example.com")]
    [InlineData("PhotoFileId:foto.png")]
    [InlineData("StorageKey:/private/4f8b")]
    [InlineData("Bytes")]
    public void BuildChangedFields_rejects_values_and_non_field_content(string candidate)
    {
        Assert.Throws<ArgumentException>(() => AuditEntry.BuildChangedFields([candidate]));
    }

    [Fact]
    public void BuildChangedFields_keeps_the_approved_field_set_within_the_persisted_limit()
    {
        var changedFields = AuditEntry.BuildChangedFields(AuditFields.All);

        Assert.NotNull(changedFields);
        Assert.True(changedFields.Length <= 500);
    }

    [Fact]
    public void Existing_auth_audit_entry_remains_valid_without_entity_target()
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "LOGIN_SUCCEEDED",
            Result = "SUCCESS",
            OccurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero),
            CorrelationId = "correlation-1"
        };

        Assert.Null(entry.TargetEntityType);
        Assert.Null(entry.TargetEntityId);
        Assert.Null(entry.ChangedFields);
    }

    [Fact]
    public void Audit_constants_cover_only_approved_target_types_and_events()
    {
        Assert.Equal("PROFESSIONAL", AuditTargetTypes.Professional);
        Assert.Equal("ROOM", AuditTargetTypes.Room);
        Assert.Equal("PROFESSIONAL_CREATED", AuditActions.ProfessionalCreated);
        Assert.Equal("PROFESSIONAL_USER_REPLACED", AuditActions.ProfessionalUserReplaced);
        Assert.Equal("ROOM_DEACTIVATED", AuditActions.RoomDeactivated);
    }
}
