using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Organization;

public readonly record struct JobId(Guid Value) : IEntityId<JobId>
{
    public static JobId From(Guid value) => new(value);
}

/// <summary>
/// A job title of the tenant's catalog (unvan), shared by all companies (D37). The code never changes and is unique
/// in the tenant.
/// </summary>
[Audited]
public sealed class Job : AuditableAggregateRoot<JobId>
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;
    public const int FamilyMaxLength = 100;
    public const int LevelMaxLength = 10;

    private Job()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Job family, e.g. "Satış" or "Bilgi Teknolojileri".</summary>
    public string? Family { get; private set; }

    /// <summary>Grade, e.g. M1 or U2.</summary>
    public string? Level { get; private set; }

    /// <summary>Inactive jobs are kept for existing positions but cannot be given to new ones.</summary>
    public bool IsActive { get; private set; }

    public static Job Create(Code code, JobProfile profile)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A job code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var job = new Job { Id = EntityId.New<JobId>(), Code = code.Value, IsActive = true };
        job.Apply(profile);
        return job;
    }

    public void Update(JobProfile profile) => Apply(profile);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private void Apply(JobProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = Text.Required(profile.Name, NameMaxLength, nameof(profile.Name));
        Family = Text.Optional(profile.Family, FamilyMaxLength, nameof(profile.Family));
        Level = Text.Optional(profile.Level, LevelMaxLength, nameof(profile.Level));
    }
}

/// <summary>Editable details of a job.</summary>
public sealed record JobProfile(string Name, string? Family, string? Level);
