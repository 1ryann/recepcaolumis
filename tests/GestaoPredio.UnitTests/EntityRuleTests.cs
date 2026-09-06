using GestaoPredio.Domain.Files;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class EntityRuleTests
{
    [Fact]
    public void Professional_create_and_update_keep_canonical_values_and_server_owned_state()
    {
        var createdAt = new DateTimeOffset(2026, 9, 5, 13, 20, 0, TimeSpan.FromHours(-4));
        var professional = Professional.Create("  Dra. Ana  Sá  ", "  Fisioterapeuta  ", "(65) 99999-1234", createdAt);

        professional.Update(" Dra. Ana  SÁ ", " Fisioterapia ", "65 98888-7777", createdAt.AddMinutes(5));

        Assert.NotEqual(Guid.Empty, professional.Id);
        Assert.Equal(" Dra. Ana  SÁ ", professional.Name);
        Assert.Equal("DRA. ANA SA", professional.NormalizedName);
        Assert.Equal(" Fisioterapia ", professional.Profession);
        Assert.Equal("FISIOTERAPIA", professional.NormalizedProfession);
        Assert.Equal("+5565988887777", professional.WhatsApp);
        Assert.True(professional.IsActive);
        Assert.Equal(createdAt.ToUniversalTime(), professional.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(5).ToUniversalTime(), professional.UpdatedAt);
    }

    [Fact]
    public void Professional_allows_identical_records_while_assigning_distinct_technical_ids()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);

        var first = Professional.Create("Ana Silva", "Fisioterapeuta", "(65) 99999-1234", occurredAt);
        var second = Professional.Create("Ana Silva", "Fisioterapeuta", "(65) 99999-1234", occurredAt);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Profession, second.Profession);
        Assert.Equal(first.WhatsApp, second.WhatsApp);
    }

    [Theory]
    [InlineData("", "Fisioterapeuta")]
    [InlineData("   ", "Fisioterapeuta")]
    [InlineData("Ana Silva", "")]
    [InlineData("Ana Silva", "   ")]
    public void Professional_rejects_missing_required_profile_fields(string name, string profession)
    {
        var occurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => Professional.Create(name, profession, "(65) 99999-1234", occurredAt));
    }

    [Fact]
    public void Professional_exposes_only_explicit_operations_for_server_owned_properties()
    {
        AssertPropertiesDoNotHavePublicSetters(
            typeof(Professional),
            nameof(Professional.NormalizedName),
            nameof(Professional.NormalizedProfession),
            nameof(Professional.PhotoFileId),
            nameof(Professional.ApplicationUserId),
            nameof(Professional.IsActive),
            nameof(Professional.CreatedAt),
            nameof(Professional.UpdatedAt),
            nameof(Professional.Version));
    }

    [Fact]
    public void Professional_status_photo_and_user_link_are_changed_only_by_named_operations()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);
        var professional = Professional.Create("Ana Silva", "Fisioterapeuta", "(65) 99999-1234", occurredAt);
        var photoFileId = Guid.NewGuid();

        professional.Deactivate(occurredAt.AddMinutes(1));
        professional.SetPhoto(photoFileId, occurredAt.AddMinutes(2));
        professional.LinkUser("identity-user-1", occurredAt.AddMinutes(3));
        professional.Activate(occurredAt.AddMinutes(4));
        professional.RemovePhoto(occurredAt.AddMinutes(5));
        professional.UnlinkUser(occurredAt.AddMinutes(6));

        Assert.True(professional.IsActive);
        Assert.Null(professional.PhotoFileId);
        Assert.Null(professional.ApplicationUserId);
        Assert.Equal(occurredAt.AddMinutes(6), professional.UpdatedAt);
    }

    [Fact]
    public void Room_create_and_update_normalize_name_validate_rates_and_start_active()
    {
        var createdAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);
        var room = Room.Create("  Sala  São José  ", "  Janela ampla  ", 150.50m, 900m, createdAt);

        room.Update("Sala São José", null, 0m, 999_999_999_999.99m, createdAt.AddMinutes(10));

        Assert.NotEqual(Guid.Empty, room.Id);
        Assert.Equal("Sala São José", room.Name);
        Assert.Equal("SALA SAO JOSE", room.NormalizedName);
        Assert.Null(room.Description);
        Assert.Equal(0m, room.HourlyRate);
        Assert.Equal(999_999_999_999.99m, room.DailyRate);
        Assert.True(room.IsActive);
        Assert.Equal(createdAt, room.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(10), room.UpdatedAt);
    }

    [Fact]
    public void Room_rejects_rate_that_would_require_rounding()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Room.Create("Sala 1", null, 10.001m, 20m, occurredAt));
    }

    [Fact]
    public void Room_rejects_a_missing_required_name()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => Room.Create("  ", null, 10m, 20m, occurredAt));
    }

    [Fact]
    public void Room_exposes_only_explicit_operations_for_server_owned_properties()
    {
        AssertPropertiesDoNotHavePublicSetters(
            typeof(Room),
            nameof(Room.NormalizedName),
            nameof(Room.IsActive),
            nameof(Room.CreatedAt),
            nameof(Room.UpdatedAt),
            nameof(Room.Version));
    }

    [Fact]
    public void Private_file_keeps_only_approved_metadata()
    {
        var createdAt = new DateTimeOffset(2026, 9, 5, 17, 20, 0, TimeSpan.Zero);
        var file = PrivateFile.Create("e4f7f3b53d2f4c0ca96dfdb72b970ab1", "image/png", 1234, PrivateFilePurposes.ProfessionalPhoto, createdAt);

        Assert.NotEqual(Guid.Empty, file.Id);
        Assert.Equal("e4f7f3b53d2f4c0ca96dfdb72b970ab1", file.StorageKey);
        Assert.Equal("image/png", file.MimeType);
        Assert.Equal(1234, file.Length);
        Assert.Equal(PrivateFilePurposes.ProfessionalPhoto, file.Purpose);
        Assert.Equal(createdAt, file.CreatedAt);
        Assert.DoesNotContain(typeof(PrivateFile).GetProperties(), property => property.Name is "FileName" or "Path" or "Bytes");
    }

    private static void AssertPropertiesDoNotHavePublicSetters(Type entityType, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = entityType.GetProperty(propertyName)!;
            Assert.False(property.SetMethod?.IsPublic ?? false, $"{entityType.Name}.{propertyName} must be server-owned.");
        }
    }
}
