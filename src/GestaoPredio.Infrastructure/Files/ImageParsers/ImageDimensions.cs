namespace GestaoPredio.Infrastructure.Files.ImageParsers;

internal readonly record struct ImageDimensions(int Width, int Height)
{
    public bool IsSafe => Width is >= 1 and <= 4096 && Height is >= 1 and <= 4096 &&
                          (long)Width * Height <= 16_777_216;
}
