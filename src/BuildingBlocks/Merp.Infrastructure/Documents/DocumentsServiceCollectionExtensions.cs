using Merp.Application.Abstractions.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Documents;

public static class DocumentsServiceCollectionExtensions
{
    public static IServiceCollection AddMerpDocuments(this IServiceCollection services)
    {
        services.TryAddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        return services;
    }
}
