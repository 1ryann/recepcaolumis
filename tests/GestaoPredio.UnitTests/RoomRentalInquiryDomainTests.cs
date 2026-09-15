using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class RoomRentalInquiryDomainTests
{
    [Fact]
    public void Create_normalizes_contact_and_snapshots_structured_availability()
    {
        var roomId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(-4)).AddTicks(9);
        var availableFrom = new DateOnly(2026, 10, 1);
        var desiredStart = new DateOnly(2026, 10, 5);
        var desiredEnd = new DateOnly(2026, 10, 20);

        var inquiry = RoomRentalInquiry.Create(roomId, "  Ana Silva  ", "(65) 99999-1234", "  Clínica Ana  ", "  Preciso à tarde.  ",
            PublicRoomAvailabilityStatus.AvailableSoon, availableFrom, occurredAt, desiredStart, desiredEnd);

        Assert.NotEqual(Guid.Empty, inquiry.Id);
        Assert.Equal(roomId, inquiry.RoomId);
        Assert.Equal("Ana Silva", inquiry.FullName);
        Assert.Equal("+5565999991234", inquiry.WhatsApp);
        Assert.Equal("Clínica Ana", inquiry.ProfessionOrCompany);
        Assert.Equal("Preciso à tarde.", inquiry.Note);
        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, inquiry.PresentedAvailabilityStatus);
        Assert.Equal(availableFrom, inquiry.PresentedAvailableFrom);
        Assert.Equal(desiredStart, inquiry.DesiredStartDate);
        Assert.Equal(desiredEnd, inquiry.DesiredEndDate);
        Assert.Equal(RoomRentalInquiryStatus.New, inquiry.Status);
        Assert.Null(inquiry.LeaseId);
        Assert.Null(inquiry.ConvertedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 13, 0, 0, TimeSpan.Zero), inquiry.CreatedAt);
    }

    [Fact]
    public void Create_turns_empty_note_into_null()
    {
        var inquiry = Create(note: "   ");

        Assert.Null(inquiry.Note);
    }

    [Theory]
    [InlineData("", "Profissão", null)]
    [InlineData("Nome", "", null)]
    [InlineData("Nome", "Profissão", "invalid-phone")]
    public void Create_rejects_missing_or_invalid_contact_data(string name, string profession, string? phone)
    {
        Assert.Throws<ArgumentException>(() => RoomRentalInquiry.Create(Guid.NewGuid(), name, phone!, profession, null,
            PublicRoomAvailabilityStatus.AvailableNow, null, DateTimeOffset.UtcNow,
            DefaultDesiredStartDate, DefaultDesiredEndDate));
    }

    [Theory]
    [InlineData(201, 1, 0)]
    [InlineData(1, 201, 0)]
    [InlineData(1, 1, 501)]
    public void Create_rejects_text_beyond_its_limit(int nameLength, int professionLength, int noteLength)
    {
        Assert.Throws<ArgumentException>(() => RoomRentalInquiry.Create(Guid.NewGuid(), new string('N', nameLength), "65999991234",
            new string('P', professionLength), noteLength == 0 ? null : new string('x', noteLength),
            PublicRoomAvailabilityStatus.AvailableNow, null, DateTimeOffset.UtcNow,
            DefaultDesiredStartDate, DefaultDesiredEndDate));
    }

    [Theory]
    [InlineData("<nota")]
    [InlineData("nota>")]
    public void Create_rejects_markup_in_note(string note)
    {
        Assert.Throws<ArgumentException>(() => Create(note));
    }

    [Theory]
    [InlineData(PublicRoomAvailabilityStatus.AvailableNow, "2026-10-01")]
    [InlineData(PublicRoomAvailabilityStatus.AvailableSoon, null)]
    public void Create_rejects_invalid_availability_pair(PublicRoomAvailabilityStatus status, string? date)
    {
        DateOnly? availableFrom = date is null ? null : DateOnly.Parse(date);

        Assert.Throws<ArgumentException>(() => RoomRentalInquiry.Create(Guid.NewGuid(), "Nome", "65999991234", "Profissão", null,
            status, availableFrom, DateTimeOffset.UtcNow, DefaultDesiredStartDate, DefaultDesiredEndDate));
    }

    [Fact]
    public void Create_accepts_desired_end_date_equal_to_start_date()
    {
        var sameDay = new DateOnly(2026, 10, 10);

        var inquiry = RoomRentalInquiry.Create(Guid.NewGuid(), "Nome", "65999991234", "Profissão", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, DateTimeOffset.UtcNow, sameDay, sameDay);

        Assert.Equal(sameDay, inquiry.DesiredStartDate);
        Assert.Equal(sameDay, inquiry.DesiredEndDate);
    }

    [Fact]
    public void Create_rejects_desired_end_date_before_start_date()
    {
        var start = new DateOnly(2026, 10, 10);
        var end = new DateOnly(2026, 10, 9);

        Assert.Throws<ArgumentException>(() => RoomRentalInquiry.Create(Guid.NewGuid(), "Nome", "65999991234", "Profissão", null,
            PublicRoomAvailabilityStatus.AvailableNow, null, DateTimeOffset.UtcNow, start, end));
    }

    [Fact]
    public void Inquiry_has_no_identity_or_concurrency_properties()
    {
        var names = typeof(RoomRentalInquiry).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("Cpf", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cnpj", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Version", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_records_lease_and_preserves_original_room()
    {
        var roomId = Guid.NewGuid();
        var inquiry = Create(roomId: roomId);
        var leaseId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.FromHours(-4)).AddTicks(5);

        inquiry.Convert(leaseId, occurredAt);

        Assert.Equal(RoomRentalInquiryStatus.Converted, inquiry.Status);
        Assert.Equal(leaseId, inquiry.LeaseId);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 19, 0, 0, TimeSpan.Zero), inquiry.ConvertedAt);
        Assert.Equal(roomId, inquiry.RoomId);
    }

    [Fact]
    public void Convert_rejects_missing_lease_and_a_second_conversion()
    {
        var inquiry = Create();

        Assert.Throws<ArgumentException>(() => inquiry.Convert(Guid.Empty, DateTimeOffset.UtcNow));

        inquiry.Convert(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => inquiry.Convert(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private static readonly DateOnly DefaultDesiredStartDate = new(2026, 10, 1);
    private static readonly DateOnly DefaultDesiredEndDate = new(2026, 10, 10);

    private static RoomRentalInquiry Create(string? note = null, Guid? roomId = null) =>
        RoomRentalInquiry.Create(roomId ?? Guid.NewGuid(), "Nome", "65999991234", "Profissão", note,
            PublicRoomAvailabilityStatus.AvailableNow, null, DateTimeOffset.UtcNow,
            DefaultDesiredStartDate, DefaultDesiredEndDate);
}
