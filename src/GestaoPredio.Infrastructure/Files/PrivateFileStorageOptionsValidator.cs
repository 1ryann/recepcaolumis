using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Files;

public sealed class PrivateFileStorageOptionsValidator : IValidateOptions<PrivateFileStorageOptions>
{
    private readonly string _environmentName;
    private readonly string _contentRoot;
    private readonly string? _webRoot;

    public PrivateFileStorageOptionsValidator(IWebHostEnvironment environment)
        : this(environment.EnvironmentName, environment.ContentRootPath, environment.WebRootPath)
    {
    }

    public PrivateFileStorageOptionsValidator(string environmentName, string contentRoot, string? webRoot)
    {
        _environmentName = environmentName;
        _contentRoot = contentRoot;
        _webRoot = webRoot;
    }

    public ValidateOptionsResult Validate(string? name, PrivateFileStorageOptions options)
    {
        if (options.ProfessionalPhotoMaxBytes is < 1 or > PrivateFileStorageOptions.MaximumProfessionalPhotoBytes)
            return Failure();
        if (string.IsNullOrWhiteSpace(options.PrivateFilesPath) || !Path.IsPathFullyQualified(options.PrivateFilesPath))
            return Failure();

        string root;
        try
        {
            root = Path.GetFullPath(options.PrivateFilesPath);
            if (Overlaps(root, Path.GetFullPath(_contentRoot)) ||
                (!string.IsNullOrWhiteSpace(_webRoot) && Overlaps(root, Path.GetFullPath(_webRoot))))
                return Failure();
            if (!Directory.Exists(root))
            {
                if (string.Equals(_environmentName, "Production", StringComparison.OrdinalIgnoreCase)) return Failure();
                Directory.CreateDirectory(root);
            }

            var probe = Path.Combine(root, $".lumis-probe-{Guid.NewGuid():N}");
            try
            {
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.WriteByte(0);
            }
            finally
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure();
        }

        options.PrivateFilesPath = root;
        return ValidateOptionsResult.Success;
    }

    private static bool Overlaps(string first, string second) =>
        IsSameOrChild(first, second) || IsSameOrChild(second, first);

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison)) return true;
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    private static ValidateOptionsResult Failure() =>
        ValidateOptionsResult.Fail("A configuração de armazenamento privado é inválida ou indisponível.");
}
