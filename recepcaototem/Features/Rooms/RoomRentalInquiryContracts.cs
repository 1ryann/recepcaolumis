using GestaoPredio.Domain.Professionals;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public sealed record RoomRentalInquiryRequest(
    string? FullName,
    string? WhatsApp,
    string? ProfessionOrCompany,
    string? Note) : IStrictModuleRequest;

public sealed record RoomRentalInquiryResult(Guid InquiryId, string WhatsappUrl, string PresentedAvailabilityLabel);

internal sealed record ValidRoomRentalInquiryInput(
    string FullName, string WhatsApp, string ProfessionOrCompany, string? Note);

/// <summary>
/// Mirrors <see cref="RoomInput"/>'s static-validator pattern. Pre-validates and maps to
/// <c>400 INVALID_ROOM_RENTAL_INQUIRY</c> here rather than letting <c>RoomRentalInquiry.Create</c>
/// throw uncaught for the same bad input (name/profession length, WhatsApp shape, note length/markup).
/// </summary>
internal static class RoomRentalInquiryInput
{
    public static bool TryValidate(RoomRentalInquiryRequest request,
        out ValidRoomRentalInquiryInput? input, out ApiError? error)
    {
        var fullName = Trim(request.FullName);
        var professionOrCompany = Trim(request.ProfessionOrCompany);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (fullName.Length is < 1 or > 200 || professionOrCompany.Length is < 1 or > 200 ||
            !WhatsAppNormalizer.TryNormalize(request.WhatsApp, out var whatsApp) ||
            note is { Length: > 500 } || note?.Contains('<') == true || note?.Contains('>') == true)
        {
            input = null;
            error = new ApiError("INVALID_ROOM_RENTAL_INQUIRY", "Os dados do interesse são inválidos.");
            return false;
        }

        input = new ValidRoomRentalInquiryInput(fullName, whatsApp, professionOrCompany, note);
        error = null;
        return true;
    }

    private static string Trim(string? value) => value?.Trim() ?? "";
}
