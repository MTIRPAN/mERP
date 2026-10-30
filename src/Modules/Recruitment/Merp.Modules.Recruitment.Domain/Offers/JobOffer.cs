using System.Text.Json.Serialization;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Offers;

public readonly record struct JobOfferId(Guid Value) : IEntityId<JobOfferId>
{
    public static JobOfferId From(Guid value) => new(value);
}

/// <summary>What the salary of an offer is paid for (PLAN R6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalaryPeriod>))]
public enum SalaryPeriod
{
    Monthly,
    Yearly,
}

/// <summary>Where an offer stands (D53).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OfferStatus>))]
public enum OfferStatus
{
    Draft,

    /// <summary>Waits for approval.</summary>
    Pending,

    /// <summary>An approver sent it back for correction.</summary>
    Returned,

    /// <summary>Approved; it may be sent to the candidate.</summary>
    Approved,

    /// <summary>The approvers turned it down.</summary>
    Rejected,

    /// <summary>The letter went to the candidate, who has not answered yet.</summary>
    Sent,

    Accepted,

    Declined,

    /// <summary>The candidate did not answer before the offer's last day.</summary>
    Expired,

    /// <summary>The company took it back before it was answered.</summary>
    Withdrawn,
}

/// <summary>Who recorded the candidate's answer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OfferResponseChannel>))]
public enum OfferResponseChannel
{
    /// <summary>HR recorded what the candidate told them.</summary>
    Hr,

    /// <summary>The candidate answered with the link in the e-mail.</summary>
    Link,
}

/// <summary>What an offer proposes, as the application checked it.</summary>
/// <param name="PositionTitle">The job offered; the opening's title by default.</param>
/// <param name="Grade">The grade or level, e.g. M2; an approval fact.</param>
/// <param name="StartDate">The first working day.</param>
/// <param name="Salary">Gross salary for the period (sensitive).</param>
/// <param name="Currency">ISO 4217 code (sensitive).</param>
/// <param name="Period">What the salary is paid for (sensitive).</param>
/// <param name="Benefits">Fringe benefits, free text.</param>
/// <param name="ExpiresOn">The last day the candidate may accept it.</param>
public sealed record OfferTerms(
    string PositionTitle,
    string? Grade,
    DateOnly StartDate,
    decimal Salary,
    string Currency,
    SalaryPeriod Period,
    string? Benefits,
    DateOnly ExpiresOn);

/// <summary>
/// A job offer to the candidate of an application (PLAN R6, D53): approved through the approval engine
/// (<c>recruitment.offer</c>), sent with its letter, answered by the candidate or recorded by HR. An accepted offer lets
/// the application be hired.
/// </summary>
[Audited]
public sealed class JobOffer : AuditableAggregateRoot<JobOfferId>, ICompanyScoped
{
    public const int NumberMaxLength = 30;
    public const int TitleMaxLength = 150;
    public const int GradeMaxLength = 20;
    public const int BenefitsMaxLength = 2000;
    public const int CommentMaxLength = 500;
    public const int CurrencyLength = 3;
    public const int TokenHashLength = 64;

    private JobOffer()
    {
    }

    /// <summary>P5 <c>recruitment.offer</c>, e.g. TKL-2026-00012.</summary>
    public string Number { get; private set; } = null!;

    public JobApplicationId ApplicationId { get; private set; }

    public JobOpeningId OpeningId { get; private set; }

    public CandidateId CandidateId { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>The unit of the opening's requisition; null without one. An approval fact.</summary>
    public Guid? OrgUnitId { get; private set; }

    /// <summary>The opening's hiring manager when the offer was made; the first approver by default.</summary>
    public Guid HiringManagerUserId { get; private set; }

    /// <summary>Who made the offer and sends it for approval.</summary>
    public Guid RequesterUserId { get; private set; }

    public string PositionTitle { get; private set; } = null!;

    public string? Grade { get; private set; }

    public DateOnly StartDate { get; private set; }

    [Sensitive]
    public decimal Salary { get; private set; }

    [Sensitive]
    public string Currency { get; private set; } = null!;

    [Sensitive]
    public SalaryPeriod Period { get; private set; }

    public string? Benefits { get; private set; }

    public DateOnly ExpiresOn { get; private set; }

    /// <summary>Above the requisition's salary range when it was sent for approval (D53).</summary>
    public bool IsAboveBudget { get; private set; }

    public OfferStatus Status { get; private set; }

    public string? ReturnComment { get; private set; }

    public string? WithdrawalReason { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When the approval ended.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The letter as it was sent (a stored PDF).</summary>
    public Guid? LetterFileId { get; private set; }

    /// <summary>
    /// SHA-256 of the candidate's answer link token (hex); the token itself is only in the e-mail. It stays after the
    /// answer, so the link shows how the offer ended.
    /// </summary>
    public string? ResponseTokenHash { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public OfferResponseChannel? RespondedVia { get; private set; }

    /// <summary>The HR user who recorded the answer; null when the candidate answered with the link.</summary>
    public Guid? RespondedByUserId { get; private set; }

    public string? ResponseNote { get; private set; }

    /// <summary>Not decided by the candidate yet: an application has at most one such offer, or an accepted one.</summary>
    public bool IsOpen => Status is OfferStatus.Draft or OfferStatus.Pending or OfferStatus.Returned or OfferStatus.Approved or OfferStatus.Sent;

    /// <summary>
    /// Above a requisition's monthly salary range (D53): a yearly salary counts as twelve months, and a currency other
    /// than the range's counts as above it since the two cannot be compared. No range, no limit.
    /// </summary>
    public bool ExceedsBudget(decimal? salaryMax, string? currency)
    {
        if (salaryMax is not { } max)
        {
            return false;
        }

        if (!string.Equals(Currency, currency, StringComparison.Ordinal))
        {
            return true;
        }

        var monthly = Period == SalaryPeriod.Yearly ? Salary / 12 : Salary;
        return monthly > max;
    }

    /// <summary>Offers an application in the pipeline a job.</summary>
    public static Result<JobOffer> Draft(string number, JobApplication application, JobOpening opening, Guid? orgUnitId, Guid requesterUserId, OfferTerms terms)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(opening);
        if (application.Status != ApplicationStatus.Active)
        {
            return JobOfferErrors.ApplicationNotActive;
        }

        var offer = new JobOffer
        {
            Id = EntityId.New<JobOfferId>(),
            Number = Text.Required(number, NumberMaxLength, nameof(number)),
            ApplicationId = application.Id,
            OpeningId = application.OpeningId,
            CandidateId = application.CandidateId,
            CompanyId = application.CompanyId,
            OrgUnitId = orgUnitId,
            HiringManagerUserId = opening.HiringManagerUserId,
            RequesterUserId = requesterUserId,
            Status = OfferStatus.Draft,
        };
        var set = offer.Set(terms);
        return set.IsFailure ? set.Error : offer;
    }

    /// <summary>Corrects a draft or a returned offer.</summary>
    public Result Update(OfferTerms terms)
    {
        if (Status is not (OfferStatus.Draft or OfferStatus.Returned))
        {
            return JobOfferErrors.NotEditable;
        }

        return Set(terms);
    }

    /// <summary>Sends a draft or a returned offer for approval; the budget check is made then.</summary>
    public Result Submit(bool aboveBudget, DateOnly today, DateTimeOffset at)
    {
        if (Status is not (OfferStatus.Draft or OfferStatus.Returned))
        {
            return JobOfferErrors.NotEditable;
        }

        if (ExpiresOn < today)
        {
            return JobOfferErrors.ExpiresInThePast;
        }

        IsAboveBudget = aboveBudget;
        Status = OfferStatus.Pending;
        ReturnComment = null;
        SubmittedAt = at;
        return Result.Success();
    }

    /// <summary>The flow ended; ignored unless it waits for approval.</summary>
    public void Complete(bool approved, DateTimeOffset at)
    {
        if (Status == OfferStatus.Pending)
        {
            Status = approved ? OfferStatus.Approved : OfferStatus.Rejected;
            DecidedAt = at;
        }
    }

    /// <summary>An approver returned it for correction; ignored unless it waits.</summary>
    public void Return(string comment)
    {
        if (Status == OfferStatus.Pending)
        {
            Status = OfferStatus.Returned;
            ReturnComment = Text.Optional(comment, CommentMaxLength, nameof(comment));
        }
    }

    /// <summary>The approved offer went to the candidate with its letter and a new answer link.</summary>
    public Result Send(Guid letterFileId, string responseTokenHash, DateOnly today, DateTimeOffset at)
    {
        if (Status != OfferStatus.Approved)
        {
            return JobOfferErrors.NotApproved;
        }

        if (ExpiresOn < today)
        {
            return JobOfferErrors.ExpiresInThePast;
        }

        if (responseTokenHash is not { Length: TokenHashLength })
        {
            throw new ArgumentException("The token hash is a SHA-256 in hex.", nameof(responseTokenHash));
        }

        Status = OfferStatus.Sent;
        LetterFileId = letterFileId;
        ResponseTokenHash = responseTokenHash;
        SentAt = at;
        return Result.Success();
    }

    /// <summary>The candidate accepted or declined a sent offer until its last day.</summary>
    public Result Respond(bool accepted, string? note, OfferResponseChannel channel, Guid? userId, DateOnly today, DateTimeOffset at)
    {
        if (Status != OfferStatus.Sent)
        {
            return JobOfferErrors.NotSent;
        }

        if (ExpiresOn < today)
        {
            return JobOfferErrors.Expired;
        }

        Status = accepted ? OfferStatus.Accepted : OfferStatus.Declined;
        RespondedAt = at;
        RespondedVia = channel;
        RespondedByUserId = channel == OfferResponseChannel.Hr ? userId : null;
        ResponseNote = Text.Optional(note, CommentMaxLength, nameof(note));
        return Result.Success();
    }

    /// <summary>Closes a sent offer the candidate did not answer by its last day; returns whether it expired.</summary>
    public bool Expire(DateOnly today)
    {
        if (Status != OfferStatus.Sent || ExpiresOn >= today)
        {
            return false;
        }

        Status = OfferStatus.Expired;
        return true;
    }

    /// <summary>
    /// The company takes back an offer the candidate has not answered. Returns whether an approval was open and has to
    /// be closed.
    /// </summary>
    public Result<bool> Withdraw(string? reason)
    {
        if (!IsOpen)
        {
            return JobOfferErrors.NotOpen;
        }

        var flowOpen = Status is OfferStatus.Pending;
        Status = OfferStatus.Withdrawn;
        WithdrawalReason = Text.Optional(reason, CommentMaxLength, nameof(reason));
        return flowOpen;
    }

    private Result Set(OfferTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Salary <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(terms), terms.Salary, "The salary is positive.");
        }

        if (terms.ExpiresOn > terms.StartDate)
        {
            return JobOfferErrors.ExpiresAfterStart;
        }

        PositionTitle = Text.Required(terms.PositionTitle, TitleMaxLength, nameof(terms.PositionTitle));
        Grade = Text.Optional(terms.Grade, GradeMaxLength, nameof(terms.Grade));
        StartDate = terms.StartDate;
        Salary = terms.Salary;
        Currency = Text.Required(terms.Currency, CurrencyLength, nameof(terms.Currency)).ToUpperInvariant();
        Period = terms.Period;
        Benefits = Text.Optional(terms.Benefits, BenefitsMaxLength, nameof(terms.Benefits));
        ExpiresOn = terms.ExpiresOn;
        return Result.Success();
    }
}

public static class JobOfferErrors
{
    public static readonly Error ApplicationNotActive = Error.Conflict("recruitment.offers.errors.application_not_active");
    public static readonly Error OpenOfferExists = Error.Conflict("recruitment.offers.errors.open_offer_exists");
    public static readonly Error NotEditable = Error.Conflict("recruitment.offers.errors.not_editable");
    public static readonly Error NotApproved = Error.Conflict("recruitment.offers.errors.not_approved");
    public static readonly Error NotSent = Error.Conflict("recruitment.offers.errors.not_sent");
    public static readonly Error NotOpen = Error.Conflict("recruitment.offers.errors.not_open");
    public static readonly Error Expired = Error.Conflict("recruitment.offers.errors.expired");
    public static readonly Error ExpiresInThePast = Error.Validation([new FieldError("expiresOn", "recruitment.offers.errors.expires_in_the_past")]);
    public static readonly Error ExpiresAfterStart = Error.Validation([new FieldError("expiresOn", "recruitment.offers.errors.expires_after_start")]);
}
