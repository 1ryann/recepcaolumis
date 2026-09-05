namespace GestaoPredio.Infrastructure.Files;

public sealed class PrivateFileStorageOptions
{
    public const string SectionName = "Storage";
    public const long DefaultProfessionalPhotoBytes = 5 * 1024 * 1024;
    public const long MaximumProfessionalPhotoBytes = 10 * 1024 * 1024;

    public string PrivateFilesPath { get; set; } = "";
    public long ProfessionalPhotoMaxBytes { get; set; } = DefaultProfessionalPhotoBytes;
}
