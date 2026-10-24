using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Approvals;
using Merp.Modules.HR.Application.Documents;
using Merp.Modules.HR.Application.SelfService;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>The approval of a leave request ended (inbox): approved, its days leave the balance; or rejected.</summary>
public sealed record CompleteLeaveRequestCommand(Guid RequestId, ApprovalOutcome Outcome, DateTimeOffset DecidedAt) : ICommand;

/// <summary>An approver returned a leave request for correction (inbox).</summary>
public sealed record ReturnLeaveRequestCommand(Guid RequestId, string Comment) : ICommand;

/// <summary>The approval of a cancellation ended (inbox): approved, the leave is cancelled and its days come back.</summary>
public sealed record CompleteLeaveCancellationCommand(Guid RequestId, ApprovalOutcome Outcome, DateTimeOffset DecidedAt) : ICommand;

/// <summary>
/// Reminds the employee and their manager of the approved leaves that start tomorrow in the company's time zone (job
/// <c>hr.leave-reminders</c>, D44), once per request. Returns how many were sent.
/// </summary>
public sealed record SendLeaveRemindersCommand : ICommand<int>, IBulkCommand;

internal sealed class CompleteLeaveRequestCommandHandler(IHrDbContext db, LeaveRequestNotifier notifier) : ICommandHandler<CompleteLeaveRequestCommand>
{
    public async Task<Result> Handle(CompleteLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.RequestId), cancellationToken);
        if (leave is not { Status: LeaveRequestStatus.Pending })
        {
            return Result.Success();
        }

        if (request.Outcome == ApprovalOutcome.Approved)
        {
            leave.Approve(request.DecidedAt);
        }
        else
        {
            leave.Reject(request.DecidedAt);
        }

        await notifier.NotifyAsync(
            HrNotificationTypes.LeaveRequestCompleted,
            leave,
            LeaveRecipients.Requester | LeaveRecipients.Employee | LeaveRecipients.Substitute,
            new Dictionary<string, string> { ["result"] = notifier.Status(leave.Status) },
            null,
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReturnLeaveRequestCommandHandler(IHrDbContext db, LeaveRequestNotifier notifier) : ICommandHandler<ReturnLeaveRequestCommand>
{
    public async Task<Result> Handle(ReturnLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var leave = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.RequestId), cancellationToken);
        if (leave is not { Status: LeaveRequestStatus.Pending })
        {
            return Result.Success();
        }

        leave.Return(request.Comment);
        await notifier.NotifyAsync(
            HrNotificationTypes.LeaveRequestReturned,
            leave,
            LeaveRecipients.Requester,
            new Dictionary<string, string> { ["comment"] = leave.ReturnComment ?? string.Empty },
            null,
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class CompleteLeaveCancellationCommandHandler(IHrDbContext db, LeaveRequestNotifier notifier) : ICommandHandler<CompleteLeaveCancellationCommand>
{
    public async Task<Result> Handle(CompleteLeaveCancellationCommand request, CancellationToken cancellationToken)
    {
        var leave = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.RequestId), cancellationToken);
        if (leave is not { Status: LeaveRequestStatus.CancellationPending })
        {
            return Result.Success();
        }

        var approved = request.Outcome == ApprovalOutcome.Approved;
        leave.CompleteCancellation(approved, request.DecidedAt);
        await notifier.NotifyAsync(
            HrNotificationTypes.LeaveCancellationCompleted,
            leave,
            LeaveRecipients.Requester | LeaveRecipients.Employee,
            new Dictionary<string, string>
            {
                ["result"] = notifier.Status(approved ? LeaveRequestStatus.Cancelled : LeaveRequestStatus.Approved),
            },
            null,
            cancellationToken);
        return Result.Success();
    }
}

/// <summary>Inbox: the approval of a leave request or of its cancellation ended.</summary>
internal sealed class LeaveApprovalCompletedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestCompleted>
{
    public async Task Handle(ApprovalRequestCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            return;
        }

        ICommand? command = integrationEvent.DocumentType switch
        {
            HrApprovalDocumentTypes.LeaveRequest => new CompleteLeaveRequestCommand(id, integrationEvent.Outcome, integrationEvent.DecidedAt),
            HrApprovalDocumentTypes.LeaveCancellation => new CompleteLeaveCancellationCommand(id, integrationEvent.Outcome, integrationEvent.DecidedAt),
            _ => null,
        };
        if (command is not null)
        {
            await ProfileChangeInbox.SendAsync(sender, command, cancellationToken);
        }
    }
}

/// <summary>Inbox: an approver returned a leave request.</summary>
internal sealed class LeaveApprovalReturnedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestReturned>
{
    public async Task Handle(ApprovalRequestReturned integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType == HrApprovalDocumentTypes.LeaveRequest && Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            await ProfileChangeInbox.SendAsync(sender, new ReturnLeaveRequestCommand(id, integrationEvent.Comment), cancellationToken);
        }
    }
}

internal sealed class SendLeaveRemindersCommandHandler(
    IHrDbContext db,
    IOrganizationLookup organization,
    IEnumerable<IApproverResolver> resolvers,
    LeaveRequestNotifier notifier,
    TimeProvider timeProvider) : ICommandHandler<SendLeaveRemindersCommand, int>
{
    public async Task<Result<int>> Handle(SendLeaveRemindersCommand request, CancellationToken cancellationToken)
    {
        // Every company is within a day of UTC: tomorrow is between the UTC day and two days after it
        var utc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var leaves = await db.LeaveRequests
            .Where(r => r.Status == LeaveRequestStatus.Approved && r.RemindedAt == null && r.StartDate >= utc && r.StartDate <= utc.AddDays(2))
            .ToListAsync(cancellationToken);
        if (leaves.Count == 0)
        {
            return 0;
        }

        var zones = (await organization.GetCompaniesAsync([.. leaves.Select(r => r.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.TimeZoneId);
        var lineManager = resolvers.Single(r => r.Key == LineManagerApproverResolver.ResolverKey);
        var sent = 0;
        foreach (var leave in leaves.Where(r => r.StartDate == HrClock.Today(timeProvider, zones.GetValueOrDefault(r.CompanyId)).AddDays(1)))
        {
            var userId = await db.Employees.AsNoTracking().Where(e => e.Id == leave.EmployeeId).Select(e => e.UserId).SingleOrDefaultAsync(cancellationToken);
            var managers = await lineManager.ResolveAsync(
                new ApproverResolutionContext(HrApprovalDocumentTypes.LeaveRequest, leave.CompanyId, userId ?? Guid.Empty, leave.EmployeeId.Value, default),
                new Dictionary<string, string> { [LineManagerApproverResolver.LevelParameter] = "1" },
                cancellationToken);
            await notifier.NotifyAsync(HrNotificationTypes.LeaveReminder, leave, LeaveRecipients.Employee, null, managers, cancellationToken);
            leave.MarkReminded(timeProvider.GetUtcNow());
            sent++;
        }

        return sent;
    }
}
