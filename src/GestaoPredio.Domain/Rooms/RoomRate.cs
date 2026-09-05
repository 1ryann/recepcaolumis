namespace GestaoPredio.Domain.Rooms;

public static class RoomRate
{
    public const decimal Maximum = 9_999_999_999_999.99m;

    public static bool IsValid(decimal value) =>
        value >= 0m && value <= Maximum && GetScale(value) <= 2;

    private static int GetScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
