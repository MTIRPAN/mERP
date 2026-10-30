using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Contracts.PreHires;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Contracts.Hiring;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Hiring;

/// <summary>
/// Asks HR to hire the candidate of an application whose offer was accepted (PLAN R7, D54): HR opens a pre-hire; the
/// application is hired when HR completes it.
/// </summary>
public sealed record HireApplicationCommand(uint Version, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>
/// Rejects the applications of an opening still in its pipeline at once, e.g. once every seat is hired for (D54); those
/// waiting for HR to complete a hire are kept. Returns how many.
/// </summary>
public sealed record RejectRemainingApplicationsCommand(string? ReasonCode, string? Note, bool SendEmail, int EmailDelayDays)
    : ICommand<int>, IRequirePermission, IBulkCommand
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid OpeningId { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Inbox: HR completed the pre-hire of an application.</summary>
public sealed record CompleteApplicationHireCommand(Guid ApplicationId) : ICommand;

/// <summary>Inbox: HR called the pre-hire of an application off.</summary>
public sealed record CancelApplicationHireRequestCommand(Guid ApplicationId) : ICommand;

internal sealed class RejectRemainingApplicationsCommandValidator : AbstractValidator<RejectRemainingApplicationsCommand>
{
    public RejectRemainingApplicationsCommandValidator()
    {
        RuleFor(c => c.ReasonCode).NotEmpty().MaximumLength(RecruitmentLookup.CodeMaxLength);
        RuleFor(c => c.Note).MaximumLength(JobApplication.NoteMaxLength);
        RuleFor(c => c.EmailDelayDays).InclusiveBetween(0, JobApplication.MaxEmailDelayDays);
    }
}

internal sealed class HireApplicationCommandHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<HireApplicationCommand>
{
    public async Task<Result> Handle(HireApplicationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await ApplicationChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (application, opening) = loaded.Value;
        var offer = await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(o => o.ApplicationId == application.Id && o.Status == OfferStatus.Accepted, cancellationToken);
        if (offer is null)
        {
            return JobApplicationErrors.OfferRequired;
        }

        var requested = application.RequestHire(offer, timeProvider.GetUtcNow());
        if (requested.IsFailure)
        {
            return requested;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var requisition = opening.RequisitionId is { } requisitionId
            ? await db.JobRequisitions.AsNoTracking().Where(r => r.Id == requisitionId).Select(r => new { r.OrgUnitId, r.PositionId }).SingleOrDefaultAsync(cancellationToken)
            : null;
        await publisher.PublishAsync(
            new CandidateHired(
                application.Id.Value,
                candidate.Id.Value,
                offer.Id.Value,
                requisition?.OrgUnitId,
                requisition?.PositionId,
                offer.PositionTitle,
                offer.StartDate,
                opening.EmploymentType.ToString(),
                candidate.FirstName,
                candidate.LastName,
                candidate.Email,
                candidate.Phone,
                candidate.EmployeeId,
                currentUser.UserId)
            {
                CompanyId = application.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class CompleteApplicationHireCommandHandler(IRecruitmentDbContext db, TimeProvider timeProvider) : ICommandHandler<CompleteApplicationHireCommand>
{
    public async Task<Result> Handle(CompleteApplicationHireCommand request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.ApplicationId), cancellationToken);
        var offer = application is null
            ? null
            : await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(o => o.ApplicationId == application.Id && o.Status == OfferStatus.Accepted, cancellationToken);
        if (application is not { Status: ApplicationStatus.Active } || offer is null)
        {
            // Already hired, or closed in the meantime: HR's record stands, the pipeline is not changed
            return Result.Success();
        }

        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        return application.Hire(opening, offer, null, null, timeProvider.GetUtcNow());
    }
}

internal sealed class CancelApplicationHireRequestCommandHandler(IRecruitmentDbContext db) : ICommandHandler<CancelApplicationHireRequestCommand>
{
    public async Task<Result> Handle(CancelApplicationHireRequestCommand request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.ApplicationId), cancellationToken);
        application?.HireRequestCancelled();
        return Result.Success();
    }
}

/// <summary>
/// A hire fills a seat of the opening's requisition, and the opening once it has hired its headcount (D54). Runs in
/// the transaction of the hire, after the single aggregate check (CLAUDE.md rule 7).
/// </summary>
internal sealed class FillSeatsOnHire(IRecruitmentDbContext db, TimeProvider timeProvider) : IDomainEventHandler<ApplicationHired>
{
    public async Task Handle(ApplicationHired domainEvent, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleAsync(o => o.Id == domainEvent.OpeningId, cancellationToken);
        if (opening.RequisitionId is { } requisitionId && await db.JobRequisitions.SingleOrDefaultAsync(r => r.Id == requisitionId, cancellationToken) is { } requisition)
        {
            requisition.Fill();
        }

        // The hire itself is saved with this transaction, so it is not counted in the database yet
        var hired = await db.JobApplications.CountAsync(
            a => a.OpeningId == opening.Id && a.Status == ApplicationStatus.Hired && a.Id != domainEvent.ApplicationId,
            cancellationToken);
        opening.MarkFilled(hired + 1, timeProvider.GetUtcNow());
    }
}

internal sealed class RejectRemainingApplicationsCommandHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    RejectionEmails emails,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RejectRemainingApplicationsCommand, int>
{
    public async Task<Result<int>> Handle(RejectRemainingApplicationsCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.OpeningId), cancellationToken);
        if (opening is null || !await access.ManagesAsync(opening.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        if (await ApplicationChanges.ReasonAsync(db, LookupKind.RejectionReason, request.ReasonCode!, cancellationToken) is not { } reason)
        {
            return ApplicationChanges.UnknownReason;
        }

        var remaining = await db.JobApplications.Include(a => a.History)
            .Where(a => a.OpeningId == opening.Id && a.Status == ApplicationStatus.Active && a.HireRequestedAt == null)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var application in remaining)
        {
            var rejected = application.Reject(opening, reason, request.Note, request.SendEmail ? now.AddDays(request.EmailDelayDays) : null, currentUser.UserId, now);
            if (rejected.IsFailure)
            {
                return rejected.Error;
            }
        }

        if (request.SendEmail && request.EmailDelayDays == 0)
        {
            await emails.SendAsync(remaining, cancellationToken);
        }

        return remaining.Count;
    }
}

/// <summary>Inbox: HR completed a pre-hire.</summary>
internal sealed class PreHireCompletedHandler(ISender sender) : IIntegrationEventHandler<PreHireCompleted>
{
    public Task Handle(PreHireCompleted integrationEvent, CancellationToken cancellationToken) =>
        RequisitionInbox.SendAsync(sender, new CompleteApplicationHireCommand(integrationEvent.ApplicationId), cancellationToken);
}

/// <summary>Inbox: HR called a pre-hire off.</summary>
internal sealed class PreHireCancelledHandler(ISender sender) : IIntegrationEventHandler<PreHireCancelled>
{
    public Task Handle(PreHireCancelled integrationEvent, CancellationToken cancellationToken) =>
        RequisitionInbox.SendAsync(sender, new CancelApplicationHireRequestCommand(integrationEvent.ApplicationId), cancellationToken);
}

/// <summary>How many applications a bulk rejection rejected.</summary>
public sealed record RemainingRejected(int Count);
