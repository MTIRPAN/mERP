using System.Text.Json;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Application.Abstractions.CustomFields;

/// <summary>
/// An entity that can carry custom fields (D12), e.g. <c>platform.company</c>. The first segment is the owning
/// module; LabelKey names it on the definition screen.
/// </summary>
public sealed record CustomFieldEntityType(string Code, string LabelKey);

/// <summary>Modules declare their entities with custom fields, like their permissions.</summary>
public interface ICustomFieldEntityProvider
{
    IEnumerable<CustomFieldEntityType> GetEntityTypes();
}

/// <summary>
/// Checks custom field input against the tenant's definitions (D12, D30); Platform implements it. The owning module
/// calls it in its command and stores the result in the entity's <c>custom_fields</c> column.
/// </summary>
public interface ICustomFieldValidator
{
    /// <summary>
    /// Values of the active definitions that apply to the company, normalized; required fields must be given.
    /// Values of inactive definitions are kept from <paramref name="current"/>. Errors name the field as
    /// <c>customFields.{code}</c>.
    /// </summary>
    /// <param name="entityType">Registered entity type, e.g. <c>platform.company</c>.</param>
    /// <param name="companyId">Company of the record; company specific fields apply only to it.</param>
    /// <param name="input">Values by field code; null keeps nothing but the inactive fields.</param>
    /// <param name="current">The record's stored values (empty for a new record).</param>
    /// <param name="cancellationToken">Request aborted.</param>
    Task<Result<CustomFieldValues>> ValidateAsync(
        string entityType,
        Guid? companyId,
        IReadOnlyDictionary<string, JsonElement>? input,
        CustomFieldValues current,
        CancellationToken cancellationToken = default);
}
