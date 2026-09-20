namespace GestaoPredio.Application.Files;

/// <summary>
/// Normalizes a room photo. Separate from <see cref="IImageNormalizer"/> (the square avatar
/// one) because a room is shown large and edge to edge: cropping it to a square, or shrinking
/// it to avatar size, throws away exactly what the visitor came to see.
/// </summary>
public interface IRoomImageNormalizer : IImageNormalizer;
