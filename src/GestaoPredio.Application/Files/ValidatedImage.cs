namespace GestaoPredio.Application.Files;

public sealed record ValidatedImage(string MimeType, long Length, int Width, int Height);
