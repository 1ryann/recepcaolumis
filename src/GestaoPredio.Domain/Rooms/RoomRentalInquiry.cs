using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.Domain.Rooms;

public sealed class RoomRentalInquiry
{
    private RoomRentalInquiry()
    {
    }

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public string FullName { get; private set; } = "";
    public string WhatsApp { get; private set; } = "";
    public string ProfessionOrCompany { get; private set; } = "";
    public string? Note { get; private set; }
    public PublicRoomAvailabilityStatus PresentedAvailabilityStatus { get; private set; }
    public DateOnly? PresentedAvailableFrom { get; private set; }
    public RoomRentalInquiryStatus Status { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? ConvertedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RoomRentalInquiry Create(Guid roomId, string fullName, string whatsApp, string professionOrCompany,
        string? note, PublicRoomAvailabilityStatus presentedAvailabilityStatus, DateOnly? presentedAvailableFrom,
        DateTimeOffset occurredAt)
    {
        var name = fullName?.Trim() ?? "";
        var occupation = professionOrCompany?.Trim() ?? "";
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (roomId == Guid.Empty || name.Length is < 1 or > 200 || occupation.Length is < 1 or > 200 ||
            !WhatsAppNormalizer.TryNormalize(whatsApp, out var canonical) || cleanNote is { Length: > 500 } ||
            cleanNote?.Contains('<') == true || cleanNote?.Contains('>') == true)
        {
            throw new ArgumentException("Os dados do interesse são inválidos.");
        }

        if (!Enum.IsDefined(presentedAvailabilityStatus) ||
            (presentedAvailabilityStatus == PublicRoomAvailabilityStatus.AvailableNow && presentedAvailableFrom != null) ||
            (presentedAvailabilityStatus == PublicRoomAvailabilityStatus.AvailableSoon && presentedAvailableFrom == null))
        {
            throw new ArgumentException("Par de disponibilidade inválido.");
        }

        return new RoomRentalInquiry
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            FullName = name,
            WhatsApp = canonical,
            ProfessionOrCompany = occupation,
            Note = cleanNote,
            PresentedAvailabilityStatus = presentedAvailabilityStatus,
            PresentedAvailableFrom = presentedAvailableFrom,
            Status = RoomRentalInquiryStatus.New,
            LeaseId = null,
            ConvertedAt = null,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };
    }

    public void Convert(Guid leaseId, DateTimeOffset occurredAt)
    {
        if (leaseId == Guid.Empty)
        {
            throw new ArgumentException("A locação deve ser informada.", nameof(leaseId));
        }

        if (Status != RoomRentalInquiryStatus.New)
        {
            throw new InvalidOperationException("Interesse já convertido.");
        }

        LeaseId = leaseId;
        ConvertedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        Status = RoomRentalInquiryStatus.Converted;
    }
}
