using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>Settings of the HR module (D35).</summary>
public sealed class HrSettingDefinitions : ISettingDefinitionProvider
{
    /// <summary>When the user of a person who left is deactivated (D6, D39).</summary>
    public const string UserDeactivation = "hr.users.deactivation";

    public const string OnTerminationDate = "on_termination_date";

    public const string Immediately = "immediately";

    /// <summary>
    /// The first day the accrual job writes entitlements on (D43); earlier ones come with the opening balances. Empty: no
    /// accruals yet.
    /// </summary>
    public const string LeaveAccrualStart = "hr.leave.accrual_start";

    public IEnumerable<SettingDefinition> GetSettings() =>
    [
        SettingDefinition.Choice(
            UserDeactivation,
            OnTerminationDate,
            [
                new SettingOption(OnTerminationDate, "hr.settings.users.deactivation.on_termination_date"),
                new SettingOption(Immediately, "hr.settings.users.deactivation.immediately"),
            ],
            "hr.settings.users.deactivation",
            "hr.settings.users.deactivation_hint",
            companyOverridable: true),
        DateSettings.Create(LeaveAccrualStart, "hr.settings.leave.accrual_start", "hr.settings.leave.accrual_start_hint", companyOverridable: true, order: 10),
    ];
}

/// <summary>
/// Asks Platform for a system user of the person (D39): the identity, the invitation and the Employee role with the
/// Self scope. The e-mail is the person's primary e-mail unless given; the user name is the e-mail unless given.
/// Platform answers through events; until then the card shows the request as pending.
/// </summary>
public sealed record CreateEmployeeUserCommand(string? UserName = null, string? Email = null) : ICommand, IRequirePermission
{
    public const int UserNameMaxLength = 255;

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.User;
}

/// <summary>Asks Platform to link the person to an existing user (D39).</summary>
public sealed record LinkEmployeeUserCommand(Guid UserId) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.User;
}

/// <summary>Asks Platform to remove the link of the person and their user; the user keeps their roles.</summary>
public sealed record UnlinkEmployeeUserCommand(Guid EmployeeId) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Employees.User;
}

/// <summary>Active users to link to a person, by name, user name or e-mail (<c>IUserDirectory</c>).</summary>
public sealed record SearchUserCandidatesQuery(string? Search) : IQuery<IReadOnlyList<UserSummary>>, IRequirePermission
{
    public const int Limit = 20;

    public string Permission => HrPermissions.Employees.User;
}

/// <summary>
/// Asks Platform to deactivate the users of people who left: no employment remains and the last one ended before
/// the day of its company (job <c>hr.user-deactivations</c>, D39). Returns how many were asked.
/// </summary>
public sealed record RequestUserDeactivationsCommand : ICommand<int>, IBulkCommand;

/// <summary>Platform linked a user to the person (inbox).</summary>
public sealed record RecordUserLinkedCommand(Guid EmployeeId, Guid UserId) : ICommand;

/// <summary>Platform removed the link of a user and the person (inbox).</summary>
public sealed record RecordUserUnlinkedCommand(Guid EmployeeId, Guid UserId) : ICommand;

/// <summary>Platform refused to create or link a user (inbox).</summary>
public sealed record RecordUserLinkFailedCommand(Guid EmployeeId, string ErrorCode) : ICommand;

internal sealed class CreateEmployeeUserCommandValidator : AbstractValidator<CreateEmployeeUserCommand>
{
    public CreateEmployeeUserCommandValidator()
    {
        RuleFor(c => c.UserName).MaximumLength(CreateEmployeeUserCommand.UserNameMaxLength);
        RuleFor(c => c.Email).Must(email => string.IsNullOrWhiteSpace(email) || SharedKernel.ValueObjects.Email.Create(email).IsSuccess)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }
}

internal sealed class LinkEmployeeUserCommandValidator : AbstractValidator<LinkEmployeeUserCommand>
{
    public LinkEmployeeUserCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}

internal static class EmployeeUserRules
{
    public const string EmailRequiredCode = "hr.employees.errors.user_email_required";

    /// <summary>The person, tracked, when one of their companies is in the caller's scope of <c>hr.employees.user</c>.</summary>
    public static async Task<Employee?> FindAsync(IHrDbContext db, IAccessScopeProvider scopes, Guid employeeId, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, HrPermissions.Employees.User, cancellationToken);
        return await EmployeeRules.FindAsync(db, scope, employeeId, cancellationToken);
    }

    /// <summary>
    /// After an employment ended: with the setting <c>immediately</c> of its company and no employment left, Platform
    /// is asked to deactivate the person's user now. Otherwise the hourly job asks after the termination date.
    /// </summary>
    public static async Task RequestImmediateDeactivationAsync(
        Employee employee,
        Guid companyId,
        ISettings settings,
        IIntegrationEventPublisher publisher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (employee.UserId is not { } userId || employee.Employments.Any(m => m.IsOpen) || employee.UserDeactivationRequestedAt is not null)
        {
            return;
        }

        var when = await settings.GetAsync<string>(HrSettingDefinitions.UserDeactivation, companyId, cancellationToken);
        if (string.Equals(when, HrSettingDefinitions.Immediately, StringComparison.Ordinal))
        {
            await publisher.PublishAsync(new UserDeactivationRequested(userId, employee.Id.Value), cancellationToken);
            employee.MarkUserDeactivationRequested(timeProvider.GetUtcNow());
        }
    }
}

internal sealed class CreateEmployeeUserCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser) : ICommandHandler<CreateEmployeeUserCommand>
{
    public async Task<Result> Handle(CreateEmployeeUserCommand request, CancellationToken cancellationToken)
    {
        var employee = await EmployeeUserRules.FindAsync(db, scopes, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? (employee.Emails.FirstOrDefault(e => e.IsPrimary) ?? (employee.Emails.Count > 0 ? employee.Emails[0] : null))?.Address
            : request.Email.Trim();
        if (email is null)
        {
            return Error.Validation([new FieldError("email", EmployeeUserRules.EmailRequiredCode)]);
        }

        var requested = employee.RequestUserLink();
        if (requested.IsFailure)
        {
            return requested;
        }

        var userName = string.IsNullOrWhiteSpace(request.UserName) ? email : request.UserName.Trim();
        await publisher.PublishAsync(
            new UserProvisioningRequested(
                employee.Id.Value,
                userName.ToLowerInvariant(),
                email,
                employee.FirstName,
                employee.LastName,
                Culture: null,
                currentUser.UserId),
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class LinkEmployeeUserCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser) : ICommandHandler<LinkEmployeeUserCommand>
{
    public async Task<Result> Handle(LinkEmployeeUserCommand request, CancellationToken cancellationToken)
    {
        var employee = await EmployeeUserRules.FindAsync(db, scopes, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        var requested = employee.RequestUserLink();
        if (requested.IsFailure)
        {
            return requested;
        }

        await publisher.PublishAsync(new UserLinkRequested(employee.Id.Value, request.UserId, currentUser.UserId), cancellationToken);
        return Result.Success();
    }
}

internal sealed class UnlinkEmployeeUserCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IIntegrationEventPublisher publisher)
    : ICommandHandler<UnlinkEmployeeUserCommand>
{
    public async Task<Result> Handle(UnlinkEmployeeUserCommand request, CancellationToken cancellationToken)
    {
        var employee = await EmployeeUserRules.FindAsync(db, scopes, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        var userId = employee.UserId;
        var requested = employee.RequestUserUnlink();
        if (requested.IsFailure)
        {
            return requested;
        }

        await publisher.PublishAsync(new UserUnlinkRequested(employee.Id.Value, userId!.Value), cancellationToken);
        return Result.Success();
    }
}

internal sealed class SearchUserCandidatesQueryHandler(IUserDirectory users) : IQueryHandler<SearchUserCandidatesQuery, IReadOnlyList<UserSummary>>
{
    public async Task<Result<IReadOnlyList<UserSummary>>> Handle(SearchUserCandidatesQuery request, CancellationToken cancellationToken)
    {
        // Users already linked to a person cannot be linked again
        var found = await users.SearchAsync(request.Search, SearchUserCandidatesQuery.Limit, cancellationToken);
        return found.Where(u => u.EmployeeId is null).ToList();
    }
}

internal sealed class RequestUserDeactivationsCommandHandler(
    IHrDbContext db,
    IOrganizationLookup organization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<RequestUserDeactivationsCommand, int>
{
    public async Task<Result<int>> Handle(RequestUserDeactivationsCommand request, CancellationToken cancellationToken)
    {
        // People with a user and no open employment whose deactivation was not asked for yet
        var candidates = await db.Employees
            .Include(e => e.Employments)
            .Where(e => e.UserId != null && e.UserDeactivationRequestedAt == null && e.Employments.All(m => m.TerminationDate != null))
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var lastEnded = candidates.ToDictionary(e => e, e => e.Employments.MaxBy(m => m.TerminationDate)!);
        var zones = (await organization.GetCompaniesAsync([.. lastEnded.Values.Select(m => m.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.TimeZoneId);
        var asked = 0;
        foreach (var (employee, employment) in lastEnded)
        {
            // The termination date is the last working day: the user may sign in until it is over
            if (employment.TerminationDate >= HrClock.Today(timeProvider, zones.GetValueOrDefault(employment.CompanyId)))
            {
                continue;
            }

            await publisher.PublishAsync(new UserDeactivationRequested(employee.UserId!.Value, employee.Id.Value), cancellationToken);
            employee.MarkUserDeactivationRequested(timeProvider.GetUtcNow());
            asked++;
        }

        return asked;
    }
}

internal sealed class RecordUserLinkedCommandHandler(IHrDbContext db) : ICommandHandler<RecordUserLinkedCommand>
{
    public async Task<Result> Handle(RecordUserLinkedCommand request, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == EmployeeId.From(request.EmployeeId), cancellationToken);
        employee?.UserLinked(request.UserId);
        return Result.Success();
    }
}

internal sealed class RecordUserUnlinkedCommandHandler(IHrDbContext db) : ICommandHandler<RecordUserUnlinkedCommand>
{
    public async Task<Result> Handle(RecordUserUnlinkedCommand request, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == EmployeeId.From(request.EmployeeId), cancellationToken);
        employee?.UserUnlinked(request.UserId);
        return Result.Success();
    }
}

internal sealed class RecordUserLinkFailedCommandHandler(IHrDbContext db) : ICommandHandler<RecordUserLinkFailedCommand>
{
    public async Task<Result> Handle(RecordUserLinkFailedCommand request, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == EmployeeId.From(request.EmployeeId), cancellationToken);
        employee?.UserLinkFailed(request.ErrorCode);
        return Result.Success();
    }
}

/// <summary>Inbox: Platform linked a user to an employee.</summary>
internal sealed class UserLinkedToEmployeeHandler(ISender sender) : IIntegrationEventHandler<UserLinkedToEmployee>
{
    public Task Handle(UserLinkedToEmployee integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new RecordUserLinkedCommand(integrationEvent.EmployeeId, integrationEvent.UserId), cancellationToken);
}

/// <summary>Inbox: Platform removed the link of a user and an employee.</summary>
internal sealed class UserUnlinkedFromEmployeeHandler(ISender sender) : IIntegrationEventHandler<UserUnlinkedFromEmployee>
{
    public Task Handle(UserUnlinkedFromEmployee integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new RecordUserUnlinkedCommand(integrationEvent.EmployeeId, integrationEvent.UserId), cancellationToken);
}

/// <summary>Inbox: Platform refused to create or link a user.</summary>
internal sealed class UserProvisioningFailedHandler(ISender sender) : IIntegrationEventHandler<UserProvisioningFailed>
{
    public Task Handle(UserProvisioningFailed integrationEvent, CancellationToken cancellationToken) =>
        EmployeeUserInbox.SendAsync(sender, new RecordUserLinkFailedCommand(integrationEvent.EmployeeId, integrationEvent.ErrorCode), cancellationToken);
}

internal static class EmployeeUserInbox
{
    public static async Task SendAsync(ISender sender, ICommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{command.GetType().Name} failed: {result.Error.Code}");
        }
    }
}
