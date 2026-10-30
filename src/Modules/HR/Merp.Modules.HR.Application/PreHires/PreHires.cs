using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.PreHires;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.HR.Domain.PreHires;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Recruitment.Contracts.Hiring;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.PreHires;

/// <summary>
/// A pre-hire in a list (D54). <c>OpenEmploymentId</c> is the person's open employment in the company: HR completes
/// such a pre-hire with a new assignment; a former or other-company employee is rehired, anyone else hired.
/// </summary>
public sealed record PreHireItem(
    Guid Id,
    Guid ApplicationId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Guid CompanyId,
    string? CompanyName,
    Guid? OrgUnitId,
    string? OrgUnitName,
    Guid? PositionId,
    string PositionTitle,
    DateOnly StartDate,
    EmploymentType EmploymentType,
    Guid? ExistingEmployeeId,
    string? ExistingEmployeeName,
    Guid? OpenEmploymentId,
    PreHireStatus Status,
    Guid? EmployeeId,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    DateTimeOffset CreatedAt);

/// <summary>Pre-hires of the companies in scope, the waiting ones first unless filtered.</summary>
public sealed record GetPreHiresQuery(PageRequest Paging, IReadOnlyList<PreHireStatus>? Statuses = null)
    : IQuery<PagedResult<PreHireItem>>, IRequirePermission
{
    public const string DefaultSort = "startDate";

    internal static readonly SortMap<PreHire> Sorts = SortMap<PreHire>.By(p => p.Id)
        .Add("startDate", p => p.StartDate)
        .Add("createdAt", p => p.CreatedAt);

    public string Permission => HrPermissions.Employees.Create;
}

public sealed record GetPreHireQuery(Guid Id) : IQuery<PreHireItem>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Create;
}

/// <summary>Calls a waiting pre-hire off; Recruitment puts the application back in the pipeline.</summary>
public sealed record CancelPreHireCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Create;
}

/// <summary>Inbox: opens the pre-hire of a candidate Recruitment hires; a second event of the application is ignored.</summary>
public sealed record OpenPreHireCommand(CandidateHired Hire) : ICommand;

/// <summary>The person among the employees with the e-mail address (D54); Infrastructure looks into the jsonb list.</summary>
public interface IEmployeeEmailMatcher
{
    Task<EmployeeId?> FindAsync(string email, CancellationToken cancellationToken);
}

/// <summary>Checks the pre-hire a hire, rehire or assignment change completes (D54).</summary>
internal static class PreHireRules
{
    /// <summary>A field error on <c>preHireId</c> when it is not a waiting pre-hire of the company, or of another person.</summary>
    public static async Task<FieldError?> CheckAsync(IHrDbContext db, Guid? preHireId, Guid companyId, EmployeeId? employeeId, CancellationToken cancellationToken)
    {
        if (preHireId is not { } id)
        {
            return null;
        }

        var preHire = await db.PreHires.AsNoTracking().SingleOrDefaultAsync(p => p.Id == PreHireId.From(id), cancellationToken);
        if (preHire is null || preHire.Status != PreHireStatus.Pending || preHire.CompanyId != companyId)
        {
            return new FieldError("preHireId", PreHireErrors.UnknownCode);
        }

        return employeeId is { } person && preHire.ExistingEmployeeId is { } existing && existing != person
            ? new FieldError("preHireId", PreHireErrors.OtherPersonCode)
            : null;
    }
}

/// <summary>Builds pre-hire rows with the names of the companies, units and existing employees.</summary>
public sealed class PreHireRows(IHrDbContext db, IOrganizationLookup organization)
{
    public async Task<List<PreHireItem>> ItemsAsync(IReadOnlyList<PreHire> preHires, CancellationToken cancellationToken)
    {
        var companies = (await organization.GetCompaniesAsync([.. preHires.Select(p => p.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        var unitIds = preHires.Where(p => p.OrgUnitId is not null).Select(p => p.OrgUnitId!.Value).Distinct().ToList();
        var units = await db.OrgUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var employeeIds = preHires.Where(p => p.ExistingEmployeeId is not null).Select(p => p.ExistingEmployeeId!.Value).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Include(e => e.Employments)
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        return
        [
            .. preHires.Select(p =>
            {
                var existing = p.ExistingEmployeeId is { } id ? employees.GetValueOrDefault(id) : null;
                var open = existing?.Employments.FirstOrDefault(m => m.IsOpen && m.CompanyId == p.CompanyId);
                return new PreHireItem(
                    p.Id.Value,
                    p.ApplicationId,
                    p.FirstName,
                    p.LastName,
                    p.Email,
                    p.Phone,
                    p.CompanyId,
                    companies.GetValueOrDefault(p.CompanyId),
                    p.OrgUnitId?.Value,
                    p.OrgUnitId is { } unit ? units.GetValueOrDefault(unit) : null,
                    p.PositionId?.Value,
                    p.PositionTitle,
                    p.StartDate,
                    p.EmploymentType,
                    existing?.Id.Value,
                    existing?.FullName,
                    open?.Id.Value,
                    p.Status,
                    p.EmployeeId?.Value,
                    p.CompletedAt,
                    p.CancelReason,
                    p.CreatedAt);
            }),
        ];
    }
}

internal sealed class GetPreHiresQueryValidator : AbstractValidator<GetPreHiresQuery>
{
    public GetPreHiresQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetPreHiresQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
    }
}

internal sealed class CancelPreHireCommandValidator : AbstractValidator<CancelPreHireCommand>
{
    public CancelPreHireCommandValidator() => RuleFor(c => c.Reason).MaximumLength(PreHire.ReasonMaxLength);
}

internal sealed class GetPreHiresQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, PreHireRows rows)
    : IQueryHandler<GetPreHiresQuery, PagedResult<PreHireItem>>
{
    public async Task<Result<PagedResult<PreHireItem>>> Handle(GetPreHiresQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var preHires = db.PreHires.AsNoTracking();
        if (!scope.IsTenantWide)
        {
            var companies = scope.CompanyIds.ToList();
            preHires = preHires.Where(p => companies.Contains(p.CompanyId));
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            preHires = preHires.Where(p => statuses.Contains(p.Status));
        }

        var page = await GetPreHiresQuery.Sorts.Apply(preHires, request.Paging.Sort, GetPreHiresQuery.DefaultSort).ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<PreHireItem>(await rows.ItemsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetPreHireQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, PreHireRows rows) : IQueryHandler<GetPreHireQuery, PreHireItem>
{
    public async Task<Result<PreHireItem>> Handle(GetPreHireQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var preHire = await db.PreHires.AsNoTracking().SingleOrDefaultAsync(p => p.Id == PreHireId.From(request.Id), cancellationToken);
        return preHire is null || !scope.CoversCompany(preHire.CompanyId)
            ? CommonErrors.NotFound
            : (await rows.ItemsAsync([preHire], cancellationToken)).Single();
    }
}

internal sealed class CancelPreHireCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IIntegrationEventPublisher publisher)
    : ICommandHandler<CancelPreHireCommand>
{
    public async Task<Result> Handle(CancelPreHireCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var preHire = await db.PreHires.SingleOrDefaultAsync(p => p.Id == PreHireId.From(request.Id), cancellationToken);
        if (preHire is null || !scope.CoversCompany(preHire.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var cancelled = preHire.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await publisher.PublishAsync(new PreHireCancelled(preHire.ApplicationId, preHire.CancelReason) { CompanyId = preHire.CompanyId }, cancellationToken);
        return Result.Success();
    }
}

internal sealed class OpenPreHireCommandHandler(IHrDbContext db, IEmployeeEmailMatcher emails) : ICommandHandler<OpenPreHireCommand>
{
    public async Task<Result> Handle(OpenPreHireCommand request, CancellationToken cancellationToken)
    {
        var hire = request.Hire;
        var companyId = hire.CompanyId ?? throw new InvalidOperationException("A hire names its company.");
        if (await db.PreHires.AnyAsync(p => p.ApplicationId == hire.ApplicationId, cancellationToken))
        {
            return Result.Success();
        }

        // Recruitment's link of an internal candidate first, then the e-mail; the TCKN is not known before HR asks for it
        var employeeId = hire.EmployeeId is { } linked && await db.Employees.AnyAsync(e => e.Id == EmployeeId.From(linked), cancellationToken)
            ? EmployeeId.From(linked)
            : await emails.FindAsync(hire.Email, cancellationToken);
        var unit = hire.OrgUnitId is { } unitId && await db.OrgUnits.AnyAsync(u => u.Id == OrgUnitId.From(unitId), cancellationToken)
            ? OrgUnitId.From(unitId)
            : (OrgUnitId?)null;
        var position = hire.PositionId is { } positionId && await db.Positions.AnyAsync(p => p.Id == PositionId.From(positionId), cancellationToken)
            ? PositionId.From(positionId)
            : (PositionId?)null;
        db.PreHires.Add(PreHire.Open(new PreHireDetails(
            hire.ApplicationId,
            hire.CandidateId,
            hire.OfferId,
            companyId,
            unit,
            position,
            hire.PositionTitle,
            hire.StartDate,
            Enum.TryParse<EmploymentType>(hire.EmploymentType, ignoreCase: false, out var type) ? type : EmploymentType.Indefinite,
            hire.FirstName,
            hire.LastName,
            hire.Email,
            hire.Phone,
            employeeId)));
        return Result.Success();
    }
}

/// <summary>The employee aggregate completed a pre-hire: the pre-hire follows and Recruitment is told (D54).</summary>
internal sealed class CompletePreHireOnConfirmation(IHrDbContext db, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<PreHireConfirmed>
{
    public async Task Handle(PreHireConfirmed domainEvent, CancellationToken cancellationToken)
    {
        var preHire = await db.PreHires.SingleAsync(p => p.Id == domainEvent.PreHireId, cancellationToken);
        if (preHire.Complete(domainEvent.EmployeeId, domainEvent.EmploymentId, timeProvider.GetUtcNow()))
        {
            await publisher.PublishAsync(
                new PreHireCompleted(preHire.ApplicationId, domainEvent.EmployeeId.Value, domainEvent.EmploymentId.Value, domainEvent.StartDate)
                {
                    CompanyId = preHire.CompanyId,
                },
                cancellationToken);
        }
    }
}

/// <summary>Inbox: Recruitment hires a candidate.</summary>
internal sealed class CandidateHiredHandler(ISender sender) : IIntegrationEventHandler<CandidateHired>
{
    public async Task Handle(CandidateHired integrationEvent, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new OpenPreHireCommand(integrationEvent), cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{nameof(OpenPreHireCommand)} failed: {result.Error.Code}");
        }
    }
}
