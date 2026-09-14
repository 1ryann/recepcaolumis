using System.Globalization;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.Application.Rooms;

public static class RoomAvailabilityFormatter
{
    public static string Format(PublicRoomAvailabilityStatus status, DateOnly? availableFrom) =>
        (status, availableFrom) switch
        {
            (PublicRoomAvailabilityStatus.AvailableNow, null) => "Disponível agora",
            (PublicRoomAvailabilityStatus.AvailableSoon, { } date) =>
                $"Disponível em breve — a partir de {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            _ => throw new ArgumentException("Par de disponibilidade inválido.")
        };
}
