using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Employees;

/// <summary>
/// Special category personal data of an employee (D13, D38): its own table and permission, one row per employee.
/// The values are encrypted by the application layer before they reach the domain; the domain never sees the plain
/// text.
/// </summary>
[Audited]
public sealed class EmployeeSensitiveData : AuditableAggregateRoot<EmployeeId>
{
    private EmployeeSensitiveData()
    {
    }

    /// <summary>Engellilik derecesi (encrypted).</summary>
    [Sensitive]
    public string? DisabilityDegree { get; private set; }

    /// <summary>Kan grubu (encrypted).</summary>
    [Sensitive]
    public string? BloodType { get; private set; }

    /// <summary>Sağlık notları (encrypted).</summary>
    [Sensitive]
    public string? HealthNotes { get; private set; }

    /// <summary>Adli sicil belgesi tarihi (encrypted).</summary>
    [Sensitive]
    public string? CriminalRecordDate { get; private set; }

    public static EmployeeSensitiveData For(EmployeeId employeeId) => new() { Id = employeeId };

    public void Update(EncryptedSensitiveValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        DisabilityDegree = values.DisabilityDegree;
        BloodType = values.BloodType;
        HealthNotes = values.HealthNotes;
        CriminalRecordDate = values.CriminalRecordDate;
    }
}

/// <summary>The encrypted sensitive values; null leaves a value empty.</summary>
public sealed record EncryptedSensitiveValues(string? DisabilityDegree, string? BloodType, string? HealthNotes, string? CriminalRecordDate);

public readonly record struct SgkExitCodeId(Guid Value) : IEntityId<SgkExitCodeId>
{
    public static SgkExitCodeId From(Guid value) => new(value);
}

/// <summary>
/// An SGK leaving reason (işten ayrılış kodu) of the tenant's list. The list is seeded and kept editable, because SGK
/// changes it from time to time (D38).
/// </summary>
[Audited]
public sealed class SgkExitCode : AuditableAggregateRoot<SgkExitCodeId>
{
    public const int CodeMaxLength = 10;

    private SgkExitCode()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static SgkExitCode Create(Code code, LocalizedString name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        return new SgkExitCode { Id = EntityId.New<SgkExitCodeId>(), Code = code.Value, Name = name, IsActive = true };
    }

    public void Rename(LocalizedString name) => Name = name ?? throw new ArgumentNullException(nameof(name));

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
