namespace GestaoPredio.Domain.Rooms;

/// <summary>
/// A comfort a room either has or does not. Deliberately a closed list rather than free
/// text so every room describes itself in the same words and each one can carry a fixed
/// icon on the catalogue. Adding one is a new member plus its code — no migration, because
/// the codes are stored as an array of strings.
/// </summary>
public enum RoomAmenity
{
    AirConditioned = 1,
    Furnished = 2,
    Wifi = 3,
    Window = 4,
    Sink = 5,
    Accessible = 6,
}

/// <summary>
/// The stored/wire form of <see cref="RoomAmenity"/>. These strings are persisted and sent
/// to the browser, so they are a contract: changing one strips the amenity from every room
/// already saved with it.
/// </summary>
public static class RoomAmenityCode
{
    public static string From(RoomAmenity amenity) => amenity switch
    {
        RoomAmenity.AirConditioned => "CLIMATIZADA",
        RoomAmenity.Furnished => "MOBILIADA",
        RoomAmenity.Wifi => "WIFI",
        RoomAmenity.Window => "JANELA",
        RoomAmenity.Sink => "PIA",
        RoomAmenity.Accessible => "ACESSIVEL",
        _ => throw new ArgumentOutOfRangeException(nameof(amenity)),
    };

    public static bool TryParse(string? code, out RoomAmenity amenity)
    {
        switch (code)
        {
            case "CLIMATIZADA": amenity = RoomAmenity.AirConditioned; return true;
            case "MOBILIADA": amenity = RoomAmenity.Furnished; return true;
            case "WIFI": amenity = RoomAmenity.Wifi; return true;
            case "JANELA": amenity = RoomAmenity.Window; return true;
            case "PIA": amenity = RoomAmenity.Sink; return true;
            case "ACESSIVEL": amenity = RoomAmenity.Accessible; return true;
            default: amenity = default; return false;
        }
    }

    public static bool IsDefined(RoomAmenity amenity) => amenity is
        >= RoomAmenity.AirConditioned and <= RoomAmenity.Accessible;
}
