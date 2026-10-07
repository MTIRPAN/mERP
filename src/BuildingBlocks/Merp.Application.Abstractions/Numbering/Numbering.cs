using Merp.SharedKernel.Numbering;

namespace Merp.Application.Abstractions.Numbering;

/// <summary>
/// A numbered document type of a module, e.g. <c>recruitment.offer</c> with <c>TKL-{yyyy}-{seq:5}</c>. The first
/// segment is the module: its schema keeps the counters. Platform seeds the default pattern (D29).
/// </summary>
public sealed record NumberSequenceDefinition(string DocumentType, string DefaultPattern, NumberResetPeriod ResetPeriod)
{
    public string Module => DocumentType.Split('.')[0];
}

/// <summary>Modules declare their numbered document types, like their permissions.</summary>
public interface INumberSequenceDefinitionProvider
{
    IEnumerable<NumberSequenceDefinition> GetSequences();
}

/// <summary>Pattern and reset period in force for a company and document type.</summary>
public sealed record NumberSequenceSettings(string Pattern, NumberResetPeriod ResetPeriod);

/// <summary>The tenant's settings of a document type: the company's own, otherwise the default. Platform implements it.</summary>
public interface INumberSequenceSettingsSource
{
    Task<NumberSequenceSettings?> GetAsync(Guid companyId, string documentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hands out document numbers (PLAN P5). The counter is incremented in the caller's transaction with a row lock, so
/// concurrent commands never get the same number and a rolled back command gives its number back.
/// </summary>
public interface INumberGenerator
{
    /// <param name="companyId">Company the document belongs to; each company counts on its own.</param>
    /// <param name="documentType">Registered document type, e.g. <c>recruitment.offer</c>.</param>
    /// <param name="date">Document date in the company's time zone; picks the period and the year in the number.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    Task<string> NextAsync(Guid companyId, string documentType, DateOnly date, CancellationToken cancellationToken = default);
}
