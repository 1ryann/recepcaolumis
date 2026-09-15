using GestaoPredio.Domain.Auditing;
using GestaoPredio.Application.Leases;

namespace GestaoPredio.UnitTests;

public sealed class AuditFieldTests
{
    [Fact]
    public void BuildChangedFields_sorts_and_deduplicates_approved_field_names()
    {
        var entry = CreateAuthEntry();

        entry.SetChangedFields([
            AuditFields.WhatsApp,
            AuditFields.Name,
            AuditFields.Profession,
            AuditFields.Name
        ]);

        Assert.Equal("Name,Profession,WhatsApp", entry.ChangedFields);
    }

    [Theory]
    [InlineData("WhatsApp:+5565999991234")]
    [InlineData("Name=ana@example.com")]
    [InlineData("PhotoFileId:foto.png")]
    [InlineData("StorageKey:/private/4f8b")]
    [InlineData("Bytes")]
    public void BuildChangedFields_rejects_values_and_non_field_content(string candidate)
    {
        var entry = CreateAuthEntry();

        Assert.Throws<ArgumentException>(() => entry.SetChangedFields([candidate]));
    }

    [Fact]
    public void BuildChangedFields_keeps_the_approved_field_set_within_the_persisted_limit()
    {
        var entry = CreateAuthEntry();

        entry.SetChangedFields([
            AuditFields.Name,
            AuditFields.Profession,
            AuditFields.WhatsApp,
            AuditFields.Description,
            AuditFields.HourlyRate,
            AuditFields.DailyRate,
            AuditFields.IsActive,
            AuditFields.PhotoFileId,
            AuditFields.ApplicationUserId
        ]);

        Assert.NotNull(entry.ChangedFields);
        Assert.True(entry.ChangedFields.Length <= 500);
    }

    [Fact]
    public void ChangedFields_can_only_be_mutated_from_approved_field_names_and_null_clears_it()
    {
        var entry = CreateAuthEntry();

        entry.SetChangedFields([AuditFields.Profession, AuditFields.Name, AuditFields.Name]);
        Assert.Equal("Name,Profession", entry.ChangedFields);

        entry.SetChangedFields(null);

        Assert.False(typeof(AuditEntry).GetProperty(nameof(AuditEntry.ChangedFields))!.SetMethod?.IsPublic ?? false);
        Assert.Null(entry.ChangedFields);
    }

    [Fact]
    public void AuditFields_does_not_expose_a_collection_that_can_expand_accepted_names()
    {
        var publicCollections = typeof(AuditFields)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => typeof(IEnumerable<string>).IsAssignableFrom(field.FieldType));

        Assert.Empty(publicCollections);
    }

    [Fact]
    public void Existing_auth_audit_entry_remains_valid_without_entity_target()
    {
        var entry = CreateAuthEntry();

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
        Assert.Equal("LEASE", AuditTargetTypes.Lease);
        Assert.Equal("LEASE_CREATED", AuditActions.LeaseCreated);
        Assert.Equal("LEASE_ENDED", AuditActions.LeaseEnded);
        Assert.Equal("ROOM_RENTAL_INQUIRY_CREATED", AuditActions.RoomRentalInquiryCreated);
        Assert.Equal("ROOM_RENTAL_INQUIRY_CONVERTED", AuditActions.RoomRentalInquiryConverted);
    }

    [Fact]
    public void Lease_update_audit_contains_only_sorted_approved_field_names()
    {
        var leaseId = Guid.NewGuid();

        var entry = LeaseAudit.CreateSucceeded(
            leaseId, AuditActions.LeaseUpdated, DateTimeOffset.UtcNow, "trace-1", "actor-1", "127.0.0.1",
            [AuditFields.RoomId, AuditFields.ContractedRate, AuditFields.TenantId]);

        Assert.Equal(AuditTargetTypes.Lease, entry.TargetEntityType);
        Assert.Equal("SUCCEEDED", entry.Result);
        Assert.Equal(leaseId, entry.TargetEntityId);
        Assert.Equal("ContractedRate,RoomId,TenantId", entry.ChangedFields);
        Assert.DoesNotContain("127.0.0.1", entry.ChangedFields);
        Assert.DoesNotContain("actor-1", entry.ChangedFields);
    }

    [Fact]
    public void Lease_audit_rejects_values_tokens_and_unapproved_fields()
    {
        Assert.Throws<ArgumentException>(() => LeaseAudit.CreateSucceeded(
            Guid.NewGuid(), AuditActions.LeaseUpdated, DateTimeOffset.UtcNow, "trace", null, null,
            ["RoomId:00000000-0000-0000-0000-000000000001"]));
        Assert.Throws<ArgumentException>(() => LeaseAudit.CreateSucceeded(
            Guid.NewGuid(), AuditActions.LeaseUpdated, DateTimeOffset.UtcNow, "trace", null, null,
            ["ConcurrencyToken"]));
    }

    private static AuditEntry CreateAuthEntry() => new()
    {
        Id = Guid.NewGuid(),
        Action = "LOGIN_SUCCEEDED",
        Result = "SUCCESS",
        OccurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero),
        CorrelationId = "correlation-1"
    };
}
