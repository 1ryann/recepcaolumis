using GestaoPredio.Infrastructure.Files;

namespace GestaoPredio.UnitTests;

public sealed class PrivateFileStorageOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Lumis-StorageOptionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Default_photo_limit_is_five_mebibytes()
    {
        Assert.Equal(5 * 1024 * 1024, new PrivateFileStorageOptions().RoomPhotoMaxBytes);
        Assert.Equal(5 * 1024 * 1024, new PrivateFileStorageOptions().ProfessionalPhotoMaxBytes);
        Assert.Equal(10 * 1024 * 1024, PrivateFileStorageOptions.MaximumProfessionalPhotoBytes);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(10485761, false)]
    [InlineData(5242880, true)]
    public void Room_photo_limit_validates_its_own_safe_range(long size, bool valid)
    {
        var privateRoot = Path.Combine(_root, "private");
        Directory.CreateDirectory(privateRoot);
        var result = Validator("Production").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = privateRoot, RoomPhotoMaxBytes = size });
        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10485761)]
    public void Size_outside_safe_range_is_rejected(long size)
    {
        Directory.CreateDirectory(_root);
        var result = Validator("Production").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = _root, ProfessionalPhotoMaxBytes = size });
        Assert.True(result.Failed);
    }

    [Fact]
    public void Missing_relative_and_nonexistent_production_roots_are_rejected()
    {
        Assert.True(Validator("Production").Validate(null, new PrivateFileStorageOptions()).Failed);
        Assert.True(Validator("Production").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = "relative" }).Failed);
        Assert.True(Validator("Production").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = Path.Combine(_root, "missing") }).Failed);
    }

    [Fact]
    public void Private_root_must_not_overlap_content_or_web_roots_in_either_direction()
    {
        var content = Path.Combine(_root, "content");
        var web = Path.Combine(content, "wwwroot");
        Directory.CreateDirectory(web);
        var validator = new PrivateFileStorageOptionsValidator("Production", content, web);

        Assert.True(validator.Validate(null, new PrivateFileStorageOptions { PrivateFilesPath = content }).Failed);
        Assert.True(validator.Validate(null, new PrivateFileStorageOptions { PrivateFilesPath = web }).Failed);
        Assert.True(validator.Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = Path.Combine(content, "private") }).Failed);
        Assert.True(validator.Validate(null, new PrivateFileStorageOptions { PrivateFilesPath = _root }).Failed);
    }

    [Fact]
    public void Testing_can_create_root_and_validation_probes_write_and_delete()
    {
        var privateRoot = Path.Combine(_root, "private");
        var result = Validator("Testing").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = privateRoot });
        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(privateRoot));
        Assert.Empty(Directory.EnumerateFiles(privateRoot));
    }

    [Fact]
    public void A_file_or_non_directory_root_is_rejected_as_non_writable_storage()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(file, "x");
        Assert.True(Validator("Production").Validate(null,
            new PrivateFileStorageOptions { PrivateFilesPath = file }).Failed);
    }

    private PrivateFileStorageOptionsValidator Validator(string environmentName)
    {
        var content = Path.Combine(_root, "app");
        var web = Path.Combine(content, "wwwroot");
        Directory.CreateDirectory(web);
        return new PrivateFileStorageOptionsValidator(environmentName, content, web);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

}
