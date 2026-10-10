using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Persistence.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Files;

public static class FilesServiceCollectionExtensions
{
    /// <summary>
    /// File storage (D16, D31): <see cref="IFileStore"/> over the storage selected by <c>Files:Storage</c>
    /// (FileSystem or S3) and the nightly sweep of deleted and orphaned content.
    /// </summary>
    public static IServiceCollection AddMerpFiles(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(
                options => options.AllowedExtensions.All(e => FileContentInspector.KnownExtensions.Contains(e.Trim().TrimStart('.').ToLowerInvariant())),
                $"Files:AllowedExtensions may only list extensions whose content is checked: {string.Join(", ", FileContentInspector.KnownExtensions)}.")
            .Validate(options => options.MaxBytes > 0, "Files:MaxBytes must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton<IFileStorage>(sp => sp.GetRequiredService<IOptions<FileStorageOptions>>().Value.Storage switch
        {
            FileStorageKind.S3 => ActivatorUtilities.CreateInstance<S3FileStorage>(sp),
            _ => ActivatorUtilities.CreateInstance<FileSystemFileStorage>(sp),
        });
        services.TryAddScoped<IFileStore, FileStore>();
        services.TryAddScoped<FileAccessPolicies>();

        services.TryAddScoped<StoredFileSweepJob>();
        services.AddSingleton(RecurringJobDefinition.For<StoredFileSweepJob>(StoredFileSweepJob.JobId, "0 2 * * *"));
        return services;
    }
}
