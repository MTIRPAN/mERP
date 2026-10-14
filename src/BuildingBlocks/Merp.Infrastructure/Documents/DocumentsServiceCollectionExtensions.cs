using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Exports;
using Merp.Infrastructure.Exports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Documents;

public static class DocumentsServiceCollectionExtensions
{
    public static IServiceCollection AddMerpDocuments(this IServiceCollection services)
    {
        services.TryAddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.TryAddSingleton<ISpreadsheet, ClosedXmlSpreadsheet>();

        // List exports to Excel (D35)
        services.AddOptions<ExportSettings>().BindConfiguration(ExportSettings.SectionName);
        services.TryAddScoped<IListExporter, ListExporter>();
        return services;
    }
}
