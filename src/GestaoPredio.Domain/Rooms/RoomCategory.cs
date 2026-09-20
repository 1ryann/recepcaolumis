namespace GestaoPredio.Domain.Rooms;

/// <summary>
/// What a room is for, shown as a single chip on the catalogue card. A closed list for the
/// same reason as <see cref="RoomAmenity"/>: one vocabulary across every room.
/// </summary>
public enum RoomCategory
{
    Consulting = 1,
    Meeting = 2,
    Creative = 3,
}

/// <summary>
/// The stored/wire form of <see cref="RoomCategory"/>. Persisted as a string and named in
/// a database check constraint, so these values are a contract.
/// </summary>
public static class RoomCategoryCode
{
    public static string From(RoomCategory category) => category switch
    {
        RoomCategory.Consulting => "CONSULTORIO",
        RoomCategory.Meeting => "REUNIAO",
        RoomCategory.Creative => "CRIATIVA",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static bool TryParse(string? code, out RoomCategory category)
    {
        switch (code)
        {
            case "CONSULTORIO": category = RoomCategory.Consulting; return true;
            case "REUNIAO": category = RoomCategory.Meeting; return true;
            case "CRIATIVA": category = RoomCategory.Creative; return true;
            default: category = default; return false;
        }
    }

    /// The `out` form cannot appear in an expression tree, which is what EF's value
    /// converter takes — hence this overload for the persistence mapping.
    public static RoomCategory? ParseOrNull(string? code) =>
        TryParse(code, out var category) ? category : null;

    public static bool IsDefined(RoomCategory category) => category is
        >= RoomCategory.Consulting and <= RoomCategory.Creative;
}
