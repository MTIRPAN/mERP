using Merp.Application.Abstractions.Numbering;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Numbering;

/// <summary>Last number handed out per document type, company and period, in each module's schema (D29).</summary>
public sealed class NumberCounter
{
    public const int DocumentTypeMaxLength = 100;
    public const int PeriodMaxLength = 10;

    public required string DocumentType { get; init; }

    public required Guid CompanyId { get; init; }

    /// <summary><c>-</c>, <c>2026</c> or <c>2026-10</c> (<see cref="NumberPattern.PeriodKey"/>).</summary>
    public required string Period { get; init; }

    public long Value { get; set; }
}

/// <summary>
/// Increments the counter of the document type's module with one atomic upsert in that module's open transaction:
/// the row lock is held until the caller commits, so concurrent commands wait instead of reading the same value,
/// and a rollback returns the number.
/// </summary>
internal sealed class NumberGenerator(
    IServiceProvider services,
    IEnumerable<ModuleDatabase> modules,
    INumberSequenceSettingsSource settings) : INumberGenerator
{
    public async Task<string> NextAsync(Guid companyId, string documentType, DateOnly date, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        var schema = documentType.Split('.')[0];
        var module = modules.FirstOrDefault(m => string.Equals(m.Schema, schema, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No module owns the document type {documentType}.");
        var db = (ModuleDbContext)services.GetRequiredService(module.DbContextType);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Document numbers are handed out inside a command's transaction.");
        }

        var sequence = await settings.GetAsync(companyId, documentType, cancellationToken)
            ?? throw new InvalidOperationException($"The document type {documentType} has no number sequence.");
        var period = NumberPattern.PeriodKey(sequence.ResetPeriod, date);

        // The schema comes from code (module registration); the values are parameters
        var sql = $$"""
            INSERT INTO "{{module.Schema}}".number_counters (document_type, company_id, period, value)
            VALUES ({0}, {1}, {2}, 1)
            ON CONFLICT (document_type, company_id, period) DO UPDATE SET value = number_counters.value + 1
            RETURNING value AS "Value"
            """;
        // Not composed (an INSERT cannot be a subquery), just read
        long? value = null;
        await foreach (var returned in db.Database.SqlQueryRaw<long>(sql, documentType, companyId, period).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            value = returned;
        }

        return NumberPattern.Format(sequence.Pattern, date, value ?? throw new InvalidOperationException("The counter upsert returned no value."));
    }
}

/// <summary>Hosts without the Platform module have no sequences; asking for a number is a programming error then.</summary>
internal sealed class NoNumberSequences : INumberSequenceSettingsSource
{
    public Task<NumberSequenceSettings?> GetAsync(Guid companyId, string documentType, CancellationToken cancellationToken = default) =>
        Task.FromResult<NumberSequenceSettings?>(null);
}
