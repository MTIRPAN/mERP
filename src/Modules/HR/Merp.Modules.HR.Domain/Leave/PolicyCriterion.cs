using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;

namespace Merp.Modules.HR.Domain.Leave;

/// <summary>What a criterion of a policy assignment rule or an extra day rule looks at (docs/domain/izin-kurallari.md §1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CriterionKind>))]
public enum CriterionKind
{
    /// <summary>The branch of the current assignment, by id.</summary>
    Branch,

    /// <summary>The employment type, e.g. PartTime.</summary>
    EmploymentType,

    /// <summary>The unit of the current position, by id; not its sub units.</summary>
    OrgUnit,

    /// <summary>The unit of the current position or one of its parent units, by id.</summary>
    OrgUnitTree,

    /// <summary>The level of the current assignment (kademe), e.g. M1.</summary>
    JobLevel,

    /// <summary>A custom field of the employee (<see cref="PolicyCriterion.FieldCode"/>), e.g. underground = true.</summary>
    CustomField,
}

/// <summary>
/// Matches when the employee's value is one of <paramref name="Values"/>, compared without case. Ids are written as
/// GUIDs, booleans as true or false.
/// </summary>
public sealed record PolicyCriterion(CriterionKind Kind, IReadOnlyList<string> Values, string? FieldCode = null)
{
    public const int MaxValues = 50;

    public bool Matches(PolicySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        IEnumerable<string?> actual = Kind switch
        {
            CriterionKind.Branch => [subject.BranchId?.ToString()],
            CriterionKind.EmploymentType => [subject.EmploymentType.ToString()],
            CriterionKind.OrgUnit => [subject.OrgUnitId?.ToString()],
            CriterionKind.OrgUnitTree => subject.OrgUnitPath.Select(id => (string?)id.ToString()),
            CriterionKind.JobLevel => [subject.JobLevel],
            CriterionKind.CustomField => [FieldCode is null ? null : subject.CustomFields.GetValueOrDefault(FieldCode)],
            _ => [],
        };
        return actual.Any(value => value is not null && Values.Any(v => string.Equals(v.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>An employment as the criteria see it on a day.</summary>
/// <param name="CompanyId">The employment's company.</param>
/// <param name="BranchId">The branch of the assignment in force.</param>
/// <param name="EmploymentType">The employment type.</param>
/// <param name="OrgUnitId">The unit of the position in force.</param>
/// <param name="OrgUnitPath">That unit and its parents, up to the top.</param>
/// <param name="JobLevel">The level of the assignment in force.</param>
/// <param name="CustomFields">The employee's custom field values as text.</param>
public sealed record PolicySubject(
    Guid CompanyId,
    Guid? BranchId,
    EmploymentType EmploymentType,
    Guid? OrgUnitId,
    IReadOnlyList<Guid> OrgUnitPath,
    string? JobLevel,
    IReadOnlyDictionary<string, string?> CustomFields);
