using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>
/// Adds a job to the tenant's catalog. The catalog is shared by the companies, so <c>hr.org.manage</c> must come
/// with the Tenant scope (D37).
/// </summary>
public sealed record CreateJobCommand(string? Code, string? Name, string? Family = null, string? Level = null)
    : ICommand<Guid>, IRequirePermission, IJobFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>Updates a job's details and active flag; the code never changes. Needs the Tenant scope (D37).</summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdateJobCommand(uint Version, string? Name, string? Family, string? Level, bool IsActive)
    : ICommand, IRequirePermission, IJobFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>Fields shared by create and update, validated by one rule set.</summary>
public interface IJobFields
{
    string? Name { get; }

    string? Family { get; }

    string? Level { get; }
}

internal static class JobRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IJobFields
    {
        validator.RuleFor(j => j.Name).NotEmpty().MaximumLength(Job.NameMaxLength);
        validator.RuleFor(j => j.Family).MaximumLength(Job.FamilyMaxLength);
        validator.RuleFor(j => j.Level).MaximumLength(Job.LevelMaxLength);
    }

    public static JobProfile ToProfile(IJobFields fields) => new(fields.Name!, fields.Family, fields.Level);

    public static async Task<bool> IsTenantWideAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        (await scopes.GetCurrentUserScopesAsync(permission, cancellationToken)).Any(s => s.Type == AccessScopeTypes.Tenant);
}

internal sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(j => j.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, Job.CodeMaxLength));
        JobRules.AddFieldRules(this);
    }
}

internal sealed class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator() => JobRules.AddFieldRules(this);
}

internal sealed class CreateJobCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<CreateJobCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        if (!await JobRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return OrgErrors.JobsNeedTenantScope;
        }

        var code = Code.Create(request.Code, Job.CodeMaxLength).Value;
        if (await db.Jobs.AnyAsync(j => j.Code == code.Value, cancellationToken))
        {
            return Error.Validation([OrgErrors.Duplicate("code")]);
        }

        var job = Job.Create(code, JobRules.ToProfile(request));
        db.Jobs.Add(job);
        return job.Id.Value;
    }
}

internal sealed class UpdateJobCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateJobCommand>
{
    public async Task<Result> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
    {
        if (!await JobRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return OrgErrors.JobsNeedTenantScope;
        }

        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == JobId.From(request.Id), cancellationToken);
        if (job is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(job).ExpectVersion(request.Version);
        job.Update(JobRules.ToProfile(request));
        if (request.IsActive)
        {
            job.Activate();
        }
        else
        {
            job.Deactivate();
        }

        return Result.Success();
    }
}
