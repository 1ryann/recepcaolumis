using GestaoPredio.Application.Abstractions;
using GestaoPredio.Application.Files;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestaoPredio.Infrastructure.Files;

public static class PrivateFileStorageRegistration
{
    public static IServiceCollection AddPrivateFileStorage(this IServiceCollection services,
        IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<PrivateFileStorageOptions>>(
            new PrivateFileStorageOptionsValidator(environment));
        services.AddOptions<PrivateFileStorageOptions>()
            .Bind(configuration.GetSection(PrivateFileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IPrivateFileStorage>(provider =>
            new FileSystemPrivateFileStorage(provider.GetRequiredService<IOptions<PrivateFileStorageOptions>>().Value));
        services.AddSingleton<IProfessionalPhotoValidator, ProfessionalPhotoValidator>();
        return services;
    }
}
