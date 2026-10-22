using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.SelfService;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.SelfService;

/// <summary>
/// A change request as its requester or HR sees it (D39). Proposed is the proposed section in the shape of the card's
/// lists; ProposedIban is masked for HR users without <c>hr.employees.sensitive.read</c> on the person.
/// </summary>
public sealed record ProfileChangeItem(
    Guid Id,
    string Number,
    ProfileChangeKind Kind,
    ProfileChangeStatus Status,
    JsonElement? Proposed,
    string? ProposedIban,
    string? Note,
    string? ReturnComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>The user's own change requests, newest first.</summary>
public sealed record GetMyProfileChangesQuery : IQuery<IReadOnlyList<ProfileChangeItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Self.ProfileRequest;
}

/// <summary>The change requests of a person in the caller's scope, newest first; HR's tab on the card.</summary>
public sealed record GetEmployeeProfileChangesQuery(Guid EmployeeId) : IQuery<IReadOnlyList<ProfileChangeItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Read;
}

/// <summary>
/// Asks for new contact lists (e-mails, phones, addresses, emergency contacts). With <see cref="RequestId"/> it
/// corrects a returned request and sends it again. Returns the request's id.
/// </summary>
public sealed record SubmitContactsChangeCommand(
    IReadOnlyList<EmailInput>? Emails,
    IReadOnlyList<PhoneInput>? Phones,
    IReadOnlyList<EmployeeAddressInput>? Addresses,
    IReadOnlyList<EmergencyContactInput>? EmergencyContacts,
    string? Note = null,
    Guid? RequestId = null) : ICommand<Guid>, IRequirePermission, IContactFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Self.ProfileRequest;
}

/// <summary>Asks for new family members; see <see cref="SubmitContactsChangeCommand"/>.</summary>
public sealed record SubmitFamilyChangeCommand(IReadOnlyList<FamilyMemberInput>? Members, string? Note = null, Guid? RequestId = null)
    : ICommand<Guid>, IRequirePermission, IFamilyFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Self.ProfileRequest;
}

/// <summary>Asks for a new IBAN, or none; see <see cref="SubmitContactsChangeCommand"/>.</summary>
public sealed record SubmitIbanChangeCommand(string? Iban, string? Note = null, Guid? RequestId = null) : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => HrPermissions.Self.ProfileRequest;
}

/// <summary>The requester withdraws an open request; Platform closes its approval.</summary>
public sealed record WithdrawProfileChangeCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Self.ProfileRequest;
}

/// <summary>The approval ended (inbox): the request is written to the card, kept off it, or rejected.</summary>
public sealed record CompleteProfileChangeCommand(Guid RequestId, ApprovalOutcome Outcome, DateTimeOffset DecidedAt) : ICommand;

/// <summary>An approver returned the request for correction (inbox).</summary>
public sealed record ReturnProfileChangeCommand(Guid RequestId, string Comment) : ICommand;

/// <summary>The approvable document type of change requests (docs/domain/yetki-ve-onay.md §2.1, D39).</summary>
public sealed class HrApprovalDocumentTypes : IApprovalDocumentTypeProvider
{
    public const string ProfileChange = "hr.profile-change";

    public IEnumerable<ApprovalDocumentType> GetDocumentTypes() =>
    [
        new(
            ProfileChange,
            "hr.approvals.document_types.profile_change",
            [new ApprovalFact("kind", ApprovalFactType.Text, "hr.approvals.facts.profile_change_kind")],
            AutoApproveWhenNoDefinition: false,
            DefaultFlow:
            [
                new ApprovalDefaultStep(
                    "hr.approvals.steps.hr_review",
                    "role",
                    new Dictionary<string, string> { [ApprovalDefaultStep.RoleCodeParameter] = "hr-specialist" },
                    AllowReturn: true),
            ]),
    ];
}

/// <summary>Who may see a change request's approval timeline besides its approvers: the requester and HR in scope.</summary>
public sealed class ProfileChangeAccessPolicy(
    IHrDbContext db,
    ICurrentUser currentUser,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IApprovalDocumentAccessPolicy
{
    public IReadOnlyCollection<string> DocumentTypes { get; } = [HrApprovalDocumentTypes.ProfileChange];

    public async Task<bool> CanReadAsync(string documentType, string documentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            return false;
        }

        var request = await db.ProfileChangeRequests.AsNoTracking()
            .Where(r => r.Id == ProfileChangeRequestId.From(id))
            .Select(r => new { r.EmployeeId, r.RequesterUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return false;
        }

        if (request.RequesterUserId == currentUser.UserId)
        {
            return true;
        }

        return await ProfileChangeRules.SeesAsync(db, scopes, holders, currentUser, organization, timeProvider, HrPermissions.Employees.Read, request.EmployeeId, cancellationToken);
    }
}

public static class ProfileChangeCommandErrors
{
    public static readonly Error OpenRequestExists = Error.Conflict("hr.profile_changes.errors.open_request_exists");
}

internal static class ProfileChangeRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The section a request replaces, as the card holds it now, in the request's JSON shape.</summary>
    public static string? Section(Employee employee, ProfileChangeKind kind) => kind switch
    {
        ProfileChangeKind.Contacts => Serialize(ContactLists.Of(employee)),
        ProfileChangeKind.Family => Serialize(employee.FamilyMembers),
        _ => null,
    };

    /// <summary>Whether the card's section is still what it was when the request was sent.</summary>
    public static bool Unchanged(ProfileChangeRequest request, Employee employee) => request.Kind switch
    {
        ProfileChangeKind.Contacts => Serialize(JsonSerializer.Deserialize<ContactLists>(request.Before!, Json)) == Section(employee, request.Kind),
        ProfileChangeKind.Family => Serialize(JsonSerializer.Deserialize<List<FamilyMember>>(request.Before!, Json)) == Section(employee, request.Kind),
        _ => request.BeforeIban == employee.Iban,
    };

    /// <summary>The company of the person's main employment: the primary open one, else the latest.</summary>
    public static Guid MainCompany(Employee employee) =>
        (employee.Employments.Where(m => m.IsOpen).OrderByDescending(m => m.IsPrimary).ThenBy(m => m.HireDate).FirstOrDefault()
            ?? employee.Employments.MaxBy(m => m.HireDate)!).CompanyId;

    public static async Task<bool> SeesAsync(
        IHrDbContext db,
        IAccessScopeProvider scopes,
        IPositionHolders holders,
        ICurrentUser currentUser,
        IOrganizationLookup organization,
        TimeProvider timeProvider,
        string permission,
        EmployeeId employeeId,
        CancellationToken cancellationToken)
    {
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var scope = await EmployeeScope.ResolveAsync(scopes, permission, db, holders, currentUser, cancellationToken);
        return await scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking().Where(e => e.Id == employeeId), days), db).AnyAsync(cancellationToken);
    }

    public static ProfileChangeItem ToItem(ProfileChangeRequest request, bool ibanVisible) =>
        new(
            request.Id.Value,
            request.Number,
            request.Kind,
            request.Status,
            request.Proposed is null ? null : JsonDocument.Parse(request.Proposed).RootElement.Clone(),
            ibanVisible ? request.ProposedIban : EmployeeMasks.Iban(request.ProposedIban),
            request.Note,
            request.ReturnComment,
            request.CreatedAt,
            request.ClosedAt);
}

internal sealed class SubmitContactsChangeCommandValidator : AbstractValidator<SubmitContactsChangeCommand>
{
    public SubmitContactsChangeCommandValidator()
    {
        ContactRules.AddContactRules(this);
        RuleFor(c => c.Note).MaximumLength(ProfileChangeRequest.NoteMaxLength);
    }
}

internal sealed class SubmitFamilyChangeCommandValidator : AbstractValidator<SubmitFamilyChangeCommand>
{
    public SubmitFamilyChangeCommandValidator()
    {
        ContactRules.AddFamilyRules(this);
        RuleFor(c => c.Note).MaximumLength(ProfileChangeRequest.NoteMaxLength);
    }
}

internal sealed class SubmitIbanChangeCommandValidator : AbstractValidator<SubmitIbanChangeCommand>
{
    public SubmitIbanChangeCommandValidator()
    {
        RuleFor(c => c.Iban).MustBeValid(Iban.Create).When(c => !string.IsNullOrWhiteSpace(c.Iban));
        RuleFor(c => c.Note).MaximumLength(ProfileChangeRequest.NoteMaxLength);
    }
}

/// <summary>Sends a new or corrected change request of the user's person for approval (D39).</summary>
public sealed class ProfileChangeSubmitter(
    IHrDbContext db,
    ICurrentUser currentUser,
    INumberGenerator numbers,
    IIntegrationEventPublisher publisher,
    IOrganizationLookup organization,
    ITextLocalizer localizer,
    TimeProvider timeProvider)
{
    public async Task<Result<Guid>> SubmitAsync(
        ProfileChangeKind kind,
        Func<Employee, ProfileChangeValues> values,
        string? note,
        Guid? requestId,
        CancellationToken cancellationToken)
    {
        var employee = await SelfServiceRules.MeAsync(db, currentUser, tracked: false, cancellationToken);
        if (employee is null || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        ProfileChangeRequest? request;
        if (requestId is { } id)
        {
            request = await db.ProfileChangeRequests.SingleOrDefaultAsync(
                r => r.Id == ProfileChangeRequestId.From(id) && r.EmployeeId == employee.Id && r.Kind == kind,
                cancellationToken);
            if (request is null)
            {
                return CommonErrors.NotFound;
            }

            var resubmitted = request.Resubmit(values(employee), note);
            if (resubmitted.IsFailure)
            {
                return resubmitted.Error;
            }
        }
        else
        {
            if (await db.ProfileChangeRequests.AnyAsync(
                    r => r.EmployeeId == employee.Id && r.Kind == kind && (r.Status == ProfileChangeStatus.Pending || r.Status == ProfileChangeStatus.Returned),
                    cancellationToken))
            {
                return ProfileChangeCommandErrors.OpenRequestExists;
            }

            var companyId = ProfileChangeRules.MainCompany(employee);
            var zone = (await organization.GetCompaniesAsync([companyId], cancellationToken)).SingleOrDefault()?.TimeZoneId;
            var number = await numbers.NextAsync(companyId, HrNumberSequences.ProfileChange, HrClock.Today(timeProvider, zone), cancellationToken);
            request = ProfileChangeRequest.Submit(number, employee.Id, companyId, userId, kind, values(employee), note);
            db.ProfileChangeRequests.Add(request);
        }

        await publisher.PublishAsync(
            new ApprovalRequested(
                HrApprovalDocumentTypes.ProfileChange,
                request.Id.Value.ToString(),
                userId,
                employee.Id.Value,
                JsonSerializer.SerializeToElement(new { kind = kind.ToString() }),
                "hr.profile_changes.approval_title",
                new Dictionary<string, string> { ["number"] = request.Number, ["employee"] = employee.FullName },
                localizer.Get($"hr.profile_changes.kind.{kind}", culture: "tr-TR"),
                $"/hr/employees/{employee.Id.Value}?tab=requests")
            {
                CompanyId = request.CompanyId,
            },
            cancellationToken);
        return request.Id.Value;
    }
}

internal sealed class SubmitContactsChangeCommandHandler(ProfileChangeSubmitter submitter) : ICommandHandler<SubmitContactsChangeCommand, Guid>
{
    public Task<Result<Guid>> Handle(SubmitContactsChangeCommand request, CancellationToken cancellationToken) =>
        submitter.SubmitAsync(
            ProfileChangeKind.Contacts,
            employee => new ProfileChangeValues(
                ProfileChangeRules.Serialize(ContactRules.ToContacts(request)),
                ProfileChangeRules.Section(employee, ProfileChangeKind.Contacts),
                null,
                null),
            request.Note,
            request.RequestId,
            cancellationToken);
}

internal sealed class SubmitFamilyChangeCommandHandler(ProfileChangeSubmitter submitter) : ICommandHandler<SubmitFamilyChangeCommand, Guid>
{
    public Task<Result<Guid>> Handle(SubmitFamilyChangeCommand request, CancellationToken cancellationToken) =>
        submitter.SubmitAsync(
            ProfileChangeKind.Family,
            employee => new ProfileChangeValues(
                ProfileChangeRules.Serialize(ContactRules.ToFamily(request)),
                ProfileChangeRules.Section(employee, ProfileChangeKind.Family),
                null,
                null),
            request.Note,
            request.RequestId,
            cancellationToken);
}

internal sealed class SubmitIbanChangeCommandHandler(ProfileChangeSubmitter submitter) : ICommandHandler<SubmitIbanChangeCommand, Guid>
{
    public Task<Result<Guid>> Handle(SubmitIbanChangeCommand request, CancellationToken cancellationToken) =>
        submitter.SubmitAsync(
            ProfileChangeKind.Iban,
            employee => new ProfileChangeValues(
                null,
                null,
                string.IsNullOrWhiteSpace(request.Iban) ? null : Iban.Create(request.Iban).Value.Value,
                employee.Iban),
            request.Note,
            request.RequestId,
            cancellationToken);
}

internal sealed class WithdrawProfileChangeCommandHandler(
    IHrDbContext db,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<WithdrawProfileChangeCommand>
{
    public async Task<Result> Handle(WithdrawProfileChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await db.ProfileChangeRequests.SingleOrDefaultAsync(
            r => r.Id == ProfileChangeRequestId.From(request.Id) && r.RequesterUserId == currentUser.UserId,
            cancellationToken);
        if (change is null)
        {
            return CommonErrors.NotFound;
        }

        var withdrawn = change.Withdraw(timeProvider.GetUtcNow());
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        await publisher.PublishAsync(new ApprovalCancellationRequested(HrApprovalDocumentTypes.ProfileChange, change.Id.Value.ToString()), cancellationToken);
        return Result.Success();
    }
}

internal sealed class GetMyProfileChangesQueryHandler(IHrDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyProfileChangesQuery, IReadOnlyList<ProfileChangeItem>>
{
    public async Task<Result<IReadOnlyList<ProfileChangeItem>>> Handle(GetMyProfileChangesQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return CommonErrors.NotFound;
        }

        var requests = await db.ProfileChangeRequests.AsNoTracking()
            .Where(r => r.EmployeeId == EmployeeId.From(employeeId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        // The person's own IBAN, like their card
        return requests.Select(r => ProfileChangeRules.ToItem(r, ibanVisible: true)).ToList();
    }
}

internal sealed class GetEmployeeProfileChangesQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    ISensitiveReadLog readLog,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeeProfileChangesQuery, IReadOnlyList<ProfileChangeItem>>
{
    public async Task<Result<IReadOnlyList<ProfileChangeItem>>> Handle(GetEmployeeProfileChangesQuery request, CancellationToken cancellationToken)
    {
        var employeeId = EmployeeId.From(request.EmployeeId);
        if (!await ProfileChangeRules.SeesAsync(db, scopes, holders, currentUser, organization, timeProvider, request.Permission, employeeId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var requests = await db.ProfileChangeRequests.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var ibanVisible = await ProfileChangeRules.SeesAsync(
            db, scopes, holders, currentUser, organization, timeProvider, HrPermissions.Employees.Sensitive.Read, employeeId, cancellationToken);
        foreach (var shown in requests.Where(r => ibanVisible && r.ProposedIban is not null))
        {
            await readLog.RecordAsync(typeof(ProfileChangeRequest), shown.Id.Value.ToString(), [nameof(ProfileChangeRequest.ProposedIban)], cancellationToken);
        }

        return requests.Select(r => ProfileChangeRules.ToItem(r, ibanVisible)).ToList();
    }
}

internal sealed class CompleteProfileChangeCommandHandler(
    IHrDbContext db,
    IAuditSource auditSource,
    IIntegrationEventPublisher publisher,
    ITextLocalizer localizer) : ICommandHandler<CompleteProfileChangeCommand>
{
    public async Task<Result> Handle(CompleteProfileChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await db.ProfileChangeRequests.SingleOrDefaultAsync(r => r.Id == ProfileChangeRequestId.From(request.RequestId), cancellationToken);
        if (change is not { IsOpen: true })
        {
            return Result.Success();
        }

        if (request.Outcome == ApprovalOutcome.Rejected)
        {
            change.Close(ProfileChangeStatus.Rejected, request.DecidedAt);
        }
        else
        {
            var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == change.EmployeeId, cancellationToken);
            if (ProfileChangeRules.Unchanged(change, employee))
            {
                // The card's changes are written by the approval, under the request's number
                auditSource.Set($"{HrApprovalDocumentTypes.ProfileChange}:{change.Number}");
                change.Close(ProfileChangeStatus.Applied, request.DecidedAt);
            }
            else
            {
                change.Close(ProfileChangeStatus.NotApplied, request.DecidedAt);
            }
        }

        await publisher.PublishAsync(
            new NotificationRequested(
                HrNotificationTypes.ProfileChangeCompleted,
                [change.RequesterUserId],
                new Dictionary<string, string>
                {
                    ["number"] = change.Number,
                    ["result"] = localizer.Get($"hr.profile_changes.status.{change.Status}", culture: "tr-TR"),
                },
                "/hr/me?tab=requests")
            {
                CompanyId = change.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

/// <summary>Writes an approved request to the card in the same transaction (CLAUDE.md rule 7).</summary>
internal sealed class ApplyApprovedProfileChange(IHrDbContext db) : IDomainEventHandler<ProfileChangeApproved>
{
    public async Task Handle(ProfileChangeApproved domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var employee = await db.Employees.SingleAsync(e => e.Id == domainEvent.EmployeeId, cancellationToken);
        switch (domainEvent.Kind)
        {
            case ProfileChangeKind.Contacts:
                JsonSerializer.Deserialize<ContactLists>(domainEvent.Proposed!, ProfileChangeRules.Json)!.ApplyTo(employee);
                break;
            case ProfileChangeKind.Family:
                employee.SetFamily(JsonSerializer.Deserialize<List<FamilyMember>>(domainEvent.Proposed!, ProfileChangeRules.Json)!);
                break;
            default:
                employee.SetIban(domainEvent.ProposedIban is { } iban ? Iban.Create(iban).Value : null);
                break;
        }
    }
}

internal sealed class ReturnProfileChangeCommandHandler(IHrDbContext db) : ICommandHandler<ReturnProfileChangeCommand>
{
    public async Task<Result> Handle(ReturnProfileChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await db.ProfileChangeRequests.SingleOrDefaultAsync(r => r.Id == ProfileChangeRequestId.From(request.RequestId), cancellationToken);
        change?.Return(request.Comment);
        return Result.Success();
    }
}

/// <summary>Inbox: the approval of a change request ended.</summary>
internal sealed class ProfileChangeCompletedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestCompleted>
{
    public async Task Handle(ApprovalRequestCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType != HrApprovalDocumentTypes.ProfileChange || !Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            return;
        }

        await ProfileChangeInbox.SendAsync(sender, new CompleteProfileChangeCommand(id, integrationEvent.Outcome, integrationEvent.DecidedAt), cancellationToken);
    }
}

/// <summary>Inbox: an approver returned a change request.</summary>
internal sealed class ProfileChangeReturnedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestReturned>
{
    public async Task Handle(ApprovalRequestReturned integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType != HrApprovalDocumentTypes.ProfileChange || !Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            return;
        }

        await ProfileChangeInbox.SendAsync(sender, new ReturnProfileChangeCommand(id, integrationEvent.Comment), cancellationToken);
    }
}

internal static class ProfileChangeInbox
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
