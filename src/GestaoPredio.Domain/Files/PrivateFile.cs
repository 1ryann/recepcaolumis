namespace GestaoPredio.Domain.Files;

public sealed class PrivateFile
{
    private PrivateFile()
    {
    }

    public Guid Id { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string MimeType { get; private set; } = "";
    public long Length { get; private set; }
    public string Purpose { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static PrivateFile Create(string storageKey, string mimeType, long length, string purpose, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (!StringComparer.Ordinal.Equals(purpose, PrivateFilePurposes.ProfessionalPhoto))
        {
            throw new ArgumentException("A finalidade do arquivo não é permitida.", nameof(purpose));
        }

        return new PrivateFile
        {
            Id = Guid.NewGuid(),
            StorageKey = storageKey,
            MimeType = mimeType,
            Length = length,
            Purpose = purpose,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
