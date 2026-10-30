using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Documents;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Offers;

/// <summary>An offer in a list; the salary is left out.</summary>
public sealed record OfferListItem(
    Guid Id,
    string Number,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid OpeningId,
    string OpeningTitle,
    Guid CompanyId,
    string PositionTitle,
    DateOnly StartDate,
    DateOnly ExpiresOn,
    OfferStatus Status,
    bool IsAboveBudget,
    DateTimeOffset? SentAt,
    DateTimeOffset? RespondedAt);

/// <summary>
/// An offer (D53). The salary, its currency and period, and the requisition's range are shown to those who manage
/// offers of the company and to its approvers; others get null and <c>SalaryVisible</c> false.
/// </summary>
public sealed record OfferDetails(
    OfferListItem Offer,
    string? Grade,
    decimal? Salary,
    string? Currency,
    SalaryPeriod? Period,
    bool SalaryVisible,
    decimal? BudgetMax,
    string? BudgetCurrency,
    string? Benefits,
    Guid HiringManagerUserId,
    string? HiringManagerName,
    Guid RequesterUserId,
    string? RequesterName,
    string? ReturnComment,
    string? WithdrawalReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    OfferResponseChannel? RespondedVia,
    string? RespondedByName,
    string? ResponseNote,
    bool CanEdit,
    bool CanSend,
    bool CanRecordResponse,
    bool CanWithdraw,
    uint Version);

/// <summary>The candidate's page of an offer, opened with the link in the e-mail (anonymous, D53).</summary>
public sealed record OfferResponsePage(
    string CompanyName,
    string CandidateName,
    string PositionTitle,
    DateOnly StartDate,
    decimal Salary,
    string Currency,
    SalaryPeriod Period,
    string? Benefits,
    DateOnly ExpiresOn,
    OfferStatus Status,
    bool CanRespond);

/// <summary>Offers of the companies in scope, newest first; of one application with <c>ApplicationId</c>.</summary>
public sealed record GetOffersQuery(PageRequest Paging, Guid? ApplicationId = null, IReadOnlyList<OfferStatus>? Statuses = null)
    : IQuery<PagedResult<OfferListItem>>, IRequirePermission
{
    public const string DefaultSort = "-createdAt";

    internal static readonly SortMap<JobOffer> Sorts = SortMap<JobOffer>.By(o => o.Id)
        .Add("createdAt", o => o.CreatedAt)
        .Add("expiresOn", o => o.ExpiresOn);

    public string Permission => RecruitmentPermissions.Offers.Read;
}

/// <summary>
/// An offer of a company in scope, or one the caller has an approval task on. From self service (<c>AsTeam</c>) the
/// opening's hiring manager reads it, with the salary only as an approver.
/// </summary>
public sealed record GetOfferQuery(Guid Id) : IQuery<OfferDetails>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Offers.Read;
}

/// <summary>The offer letter: the one sent, or a preview before sending. It shows the salary, so salary rules apply.</summary>
public sealed record GetOfferLetterQuery(Guid Id) : IQuery<RecruitmentFileDownload>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Offers.Read;
}

/// <summary>Drafts an offer for an application in the pipeline; returns its id.</summary>
public sealed record CreateOfferCommand(
    Guid ApplicationId,
    string? PositionTitle,
    string? Grade,
    DateOnly StartDate,
    decimal Salary,
    string? Currency,
    SalaryPeriod Period,
    string? Benefits,
    DateOnly ExpiresOn) : ICommand<Guid>, IRequirePermission, IOfferFields
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>Corrects a draft or a returned offer.</summary>
public sealed record UpdateOfferCommand(
    uint Version,
    string? PositionTitle,
    string? Grade,
    DateOnly StartDate,
    decimal Salary,
    string? Currency,
    SalaryPeriod Period,
    string? Benefits,
    DateOnly ExpiresOn) : ICommand, IRequirePermission, IOfferFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>Sends a draft or a returned offer for approval (<c>recruitment.offer</c>).</summary>
public sealed record SubmitOfferCommand(uint Version) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>Sends an approved offer to the candidate: the letter as a PDF and the answer link.</summary>
public sealed record SendOfferCommand(uint Version) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>HR records the candidate's answer to a sent offer.</summary>
public sealed record RecordOfferResponseCommand(uint Version, bool Accepted, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>Takes back an offer the candidate has not answered; an open approval is closed.</summary>
public sealed record WithdrawOfferCommand(uint Version, string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Offers.Manage;
}

/// <summary>The candidate's page of an offer by the token of the link (anonymous).</summary>
public sealed record GetOfferResponsePageQuery(string Token) : IQuery<OfferResponsePage>;

/// <summary>The candidate accepts or declines an offer with the link (anonymous).</summary>
public sealed record RespondToOfferCommand(bool Accepted, string? Note) : ICommand
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;
}

/// <summary>Inbox: the approval of an offer ended.</summary>
public sealed record CompleteOfferCommand(Guid OfferId, ApprovalOutcome Outcome, DateTimeOffset DecidedAt) : ICommand;

/// <summary>Inbox: an approver returned an offer for correction.</summary>
public sealed record ReturnOfferCommand(Guid OfferId, string Comment) : ICommand;

/// <summary>Closes the sent offers past their last day (the offer expiry job, D53); returns how many.</summary>
public sealed record ExpireOffersCommand : ICommand<int>, IBulkCommand;

/// <summary>The fields an offer is made of.</summary>
public interface IOfferFields
{
    string? PositionTitle { get; }

    string? Grade { get; }

    DateOnly StartDate { get; }

    decimal Salary { get; }

    string? Currency { get; }

    SalaryPeriod Period { get; }

    string? Benefits { get; }

    DateOnly ExpiresOn { get; }
}

/// <summary>The approvable document type of offers (docs/domain/yetki-ve-onay.md §2.8, D53).</summary>
public sealed class OfferApprovalDocumentTypes : IApprovalDocumentTypeProvider
{
    /// <summary>The above-budget step's condition (§2.3).</summary>
    public const string AboveBudgetCondition = """{"fact":"isAboveBudget","op":"eq","value":true}""";

    public IEnumerable<ApprovalDocumentType> GetDocumentTypes() =>
    [
        new(
            RecruitmentDocumentTypes.Offer,
            "recruitment.approvals.document_types.offer",
            [
                new ApprovalFact("salary", ApprovalFactType.Number, "recruitment.approvals.facts.salary"),
                new ApprovalFact("grade", ApprovalFactType.Text, "recruitment.approvals.facts.grade"),
                new ApprovalFact("companyId", ApprovalFactType.Identifier, "recruitment.approvals.facts.company"),
                new ApprovalFact("orgUnitId", ApprovalFactType.Identifier, "recruitment.approvals.facts.org_unit"),
                new ApprovalFact("hiringManagerUserId", ApprovalFactType.Identifier, "recruitment.approvals.facts.hiring_manager"),
                new ApprovalFact("isAboveBudget", ApprovalFactType.Boolean, "recruitment.approvals.facts.is_above_budget"),
            ],
            AutoApproveWhenNoDefinition: false,
            DefaultFlow:
            [
                new ApprovalDefaultStep(
                    "recruitment.approvals.steps.hiring_manager",
                    "fact-user",
                    new Dictionary<string, string> { ["factName"] = "hiringManagerUserId" },
                    AllowReturn: true),
                new ApprovalDefaultStep(
                    "recruitment.approvals.steps.hr_manager",
                    "role",
                    new Dictionary<string, string> { [ApprovalDefaultStep.RoleCodeParameter] = "hr-manager" },
                    AllowReturn: true),
                new ApprovalDefaultStep(
                    "recruitment.approvals.steps.unit_manager",
                    "org-unit-manager",
                    new Dictionary<string, string> { ["factName"] = "orgUnitId" },
                    AboveBudgetCondition,
                    AllowReturn: true),
            ]),
    ];
}

/// <summary>Notifications of offers for those who make them (D53).</summary>
public sealed class OfferNotificationTypes : INotificationTypeProvider
{
    public const string Completed = "recruitment.offer_completed";
    public const string Returned = "recruitment.offer_returned";
    public const string Answered = "recruitment.offer_answered";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Completed, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Returned, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(Answered, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>Who sees an offer's approval timeline besides its approvers: readers of offers of its company.</summary>
public sealed class OfferAccessPolicy(IRecruitmentDbContext db, IAccessScopeProvider scopes) : IApprovalDocumentAccessPolicy
{
    public IReadOnlyCollection<string> DocumentTypes { get; } = [RecruitmentDocumentTypes.Offer];

    public async Task<bool> CanReadAsync(string documentType, string documentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            return false;
        }

        var companyId = await db.JobOffers.AsNoTracking().Where(o => o.Id == JobOfferId.From(id)).Select(o => (Guid?)o.CompanyId).SingleOrDefaultAsync(cancellationToken);
        return companyId is { } company && await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Offers.Read, company, cancellationToken);
    }
}

/// <summary>Who reaches offers (D53).</summary>
public sealed class OfferAccess(IAccessScopeProvider scopes, ICurrentUser currentUser, IApprovalAccess approvals)
{
    public Task<bool> ReadsAsync(Guid companyId, CancellationToken cancellationToken) =>
        RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Offers.Read, companyId, cancellationToken);

    public Task<bool> ManagesAsync(Guid companyId, CancellationToken cancellationToken) =>
        RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Offers.Manage, companyId, cancellationToken);

    /// <summary>A pending or past approval task on the offer (docs/domain/yetki-ve-onay.md §1, rule 5).</summary>
    public async Task<bool> ApprovesAsync(JobOffer offer, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId && await approvals.HasTaskAsync(userId, RecruitmentDocumentTypes.Offer, offer.Id.Value.ToString(), cancellationToken);
}

public static class OfferErrors
{
    public const string UnknownApplicationCode = "recruitment.offers.errors.unknown_application";
    public const string CurrencyCode = "recruitment.offers.errors.currency";
}

/// <summary>Checks of the offer fields shared by drafting and correcting.</summary>
internal static class OfferRules
{
    public const decimal MaxSalary = 1_000_000_000m;

    public static OfferTerms Terms(IOfferFields fields, string defaultTitle) => new(
        string.IsNullOrWhiteSpace(fields.PositionTitle) ? defaultTitle : fields.PositionTitle,
        fields.Grade,
        fields.StartDate,
        fields.Salary,
        fields.Currency!.Trim(),
        fields.Period,
        fields.Benefits,
        fields.ExpiresOn);

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IOfferFields
    {
        validator.RuleFor(o => o.PositionTitle).MaximumLength(JobOffer.TitleMaxLength);
        validator.RuleFor(o => o.Grade).MaximumLength(JobOffer.GradeMaxLength);
        validator.RuleFor(o => o.StartDate).NotEmpty();
        validator.RuleFor(o => o.Salary).GreaterThan(0).LessThan(MaxSalary).PrecisionScale(14, 2, ignoreTrailingZeros: true);
        validator.RuleFor(o => o.Currency).NotEmpty()
            .Must(c => c is { Length: JobOffer.CurrencyLength } && c.All(char.IsAsciiLetter))
            .WithErrorCode(OfferErrors.CurrencyCode);
        validator.RuleFor(o => o.Period).IsInEnum();
        validator.RuleFor(o => o.Benefits).MaximumLength(JobOffer.BenefitsMaxLength);
        validator.RuleFor(o => o.ExpiresOn).NotEmpty();
    }

    /// <summary>The token of a new answer link (256 bits, URL safe) and its SHA-256 in hex.</summary>
    public static (string Token, string Hash) NewToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static async Task<DateOnly> TodayAsync(IOrganizationLookup organization, TimeProvider timeProvider, Guid companyId, CancellationToken cancellationToken)
    {
        var company = (await organization.GetCompaniesAsync([companyId], cancellationToken)).SingleOrDefault();
        return RecruitmentClock.Today(timeProvider, company?.TimeZoneId);
    }

    /// <summary>A salary paid for a year counts as twelve months in the approval facts.</summary>
    public static decimal Monthly(JobOffer offer) => offer.Period == SalaryPeriod.Yearly ? Math.Round(offer.Salary / 12, 2) : offer.Salary;
}

internal static class OfferChanges
{
    public static async Task<JobOffer?> LoadAsync(IRecruitmentDbContext db, OfferAccess access, Guid id, uint version, CancellationToken cancellationToken)
    {
        var offer = await db.JobOffers.SingleOrDefaultAsync(o => o.Id == JobOfferId.From(id), cancellationToken);
        if (offer is null || !await access.ManagesAsync(offer.CompanyId, cancellationToken))
        {
            return null;
        }

        db.Entry(offer).ExpectVersion(version);
        return offer;
    }
}

/// <summary>Builds list rows of offers with the candidates' names and the openings' titles.</summary>
public sealed class OfferRows(IRecruitmentDbContext db)
{
    public async Task<List<OfferListItem>> ItemsAsync(IReadOnlyList<JobOffer> offers, CancellationToken cancellationToken)
    {
        var candidateIds = offers.Select(o => o.CandidateId).Distinct().ToList();
        var candidates = await db.Candidates.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);
        var openingIds = offers.Select(o => o.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Title, cancellationToken);
        return
        [
            .. offers.Select(o => new OfferListItem(
                o.Id.Value,
                o.Number,
                o.ApplicationId.Value,
                o.CandidateId.Value,
                candidates.GetValueOrDefault(o.CandidateId, string.Empty),
                o.OpeningId.Value,
                openings.GetValueOrDefault(o.OpeningId, string.Empty),
                o.CompanyId,
                o.PositionTitle,
                o.StartDate,
                o.ExpiresOn,
                o.Status,
                o.IsAboveBudget,
                o.SentAt,
                o.RespondedAt)),
        ];
    }
}

/// <summary>The offer letter (D36, D53): the built-in or the tenant's template of <c>recruitment.offer_letter</c>.</summary>
public sealed class OfferLetters(
    IRecruitmentDbContext db,
    IDocumentGenerator generator,
    ILocalizationSource localization,
    ITextLocalizer localizer,
    TimeProvider timeProvider)
{
    public static readonly FileRules Rules = new(new HashSet<string>(["pdf"], StringComparer.Ordinal), 10 * 1024 * 1024);

    /// <summary>The letter in the candidate's language, the tenant's when the candidate has none.</summary>
    public async Task<Result<(byte[] Content, string FileName, string Culture)>> PrintAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == offer.CandidateId, cancellationToken);
        var culture = candidate.Culture ?? (await localization.GetAsync(cancellationToken)).DefaultCulture;
        var format = CultureInfo.GetCultureInfo(culture);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["candidate"] = OfferLetterType.Row(("name", candidate.FullName), ("email", candidate.Email)),
            ["offer"] = OfferLetterType.Row(
                ("number", offer.Number),
                ("date", DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).ToString("d", format)),
                ("position", offer.PositionTitle),
                ("grade", offer.Grade ?? string.Empty),
                ("start_date", offer.StartDate.ToString("d", format)),
                ("salary", Money(offer.Salary, offer.Currency, format)),
                ("period", localizer.Get($"recruitment.offers.periods.{offer.Period}", culture: culture)),
                ("benefits", offer.Benefits ?? string.Empty),
                ("expires_on", offer.ExpiresOn.ToString("d", format))),
        };
        var printed = await generator.GetPdfAsync(new DocumentRequest(OfferLetterType.Key, offer.CompanyId, culture, model), cancellationToken);
        if (printed.IsFailure)
        {
            return printed.Error;
        }

        var fileName = localizer.Get("recruitment.documents.offer_letter.file_name", new Dictionary<string, object?> { ["number"] = offer.Number }, culture);
        return (printed.Value.Content, fileName, culture);
    }

    public static string Money(decimal amount, string currency, CultureInfo format) =>
        string.Create(format, $"{amount:N2} {currency}");
}

internal sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        OfferRules.AddFieldRules(this);
    }
}

internal sealed class UpdateOfferCommandValidator : AbstractValidator<UpdateOfferCommand>
{
    public UpdateOfferCommandValidator() => OfferRules.AddFieldRules(this);
}

internal sealed class RecordOfferResponseCommandValidator : AbstractValidator<RecordOfferResponseCommand>
{
    public RecordOfferResponseCommandValidator() => RuleFor(c => c.Note).MaximumLength(JobOffer.CommentMaxLength);
}

internal sealed class RespondToOfferCommandValidator : AbstractValidator<RespondToOfferCommand>
{
    public RespondToOfferCommandValidator() => RuleFor(c => c.Note).MaximumLength(JobOffer.CommentMaxLength);
}

internal sealed class WithdrawOfferCommandValidator : AbstractValidator<WithdrawOfferCommand>
{
    public WithdrawOfferCommandValidator() => RuleFor(c => c.Reason).MaximumLength(JobOffer.CommentMaxLength);
}

internal sealed class GetOffersQueryValidator : AbstractValidator<GetOffersQuery>
{
    public GetOffersQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetOffersQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
    }
}

internal sealed class GetOffersQueryHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, OfferRows rows)
    : IQueryHandler<GetOffersQuery, PagedResult<OfferListItem>>
{
    public async Task<Result<PagedResult<OfferListItem>>> Handle(GetOffersQuery request, CancellationToken cancellationToken)
    {
        var offers = db.JobOffers.AsNoTracking();
        if (await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken) is { } companies)
        {
            var ids = companies.ToList();
            offers = offers.Where(o => ids.Contains(o.CompanyId));
        }

        if (request.ApplicationId is { } applicationId)
        {
            offers = offers.Where(o => o.ApplicationId == JobApplicationId.From(applicationId));
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            offers = offers.Where(o => statuses.Contains(o.Status));
        }

        var page = await GetOffersQuery.Sorts.Apply(offers, request.Paging.Sort, GetOffersQuery.DefaultSort).ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<OfferListItem>(await rows.ItemsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetOfferQueryHandler(
    IRecruitmentDbContext db,
    OfferAccess access,
    ICurrentUser currentUser,
    OfferRows rows,
    IUserDirectory users,
    ISensitiveReadLog readLog) : IQueryHandler<GetOfferQuery, OfferDetails>
{
    public async Task<Result<OfferDetails>> Handle(GetOfferQuery request, CancellationToken cancellationToken)
    {
        var found = await db.JobOffers.AsNoTracking()
            .Where(o => o.Id == JobOfferId.From(request.Id))
            .Select(o => new { Offer = o, Version = EF.Property<uint>(o, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return CommonErrors.NotFound;
        }

        var offer = found.Offer;
        var reads = request.AsTeam
            ? currentUser.UserId is { } me && offer.HiringManagerUserId == me
            : await access.ReadsAsync(offer.CompanyId, cancellationToken);
        var approves = await access.ApprovesAsync(offer, cancellationToken);
        if (!reads && !approves)
        {
            return CommonErrors.NotFound;
        }

        var manages = !request.AsTeam && await access.ManagesAsync(offer.CompanyId, cancellationToken);
        var salaryVisible = manages || approves;
        JobRequisition? requisition = null;
        if (salaryVisible)
        {
            await readLog.RecordAsync(typeof(JobOffer), offer.Id.Value.ToString(), [nameof(JobOffer.Salary), nameof(JobOffer.Currency), nameof(JobOffer.Period)], cancellationToken);
            var requisitionId = await db.JobOpenings.AsNoTracking().Where(o => o.Id == offer.OpeningId).Select(o => o.RequisitionId).SingleAsync(cancellationToken);
            requisition = requisitionId is { } rid ? await db.JobRequisitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rid, cancellationToken) : null;
        }

        var people = (await users.GetAsync([.. new[] { offer.HiringManagerUserId, offer.RequesterUserId, offer.RespondedByUserId ?? Guid.Empty }.Distinct()], cancellationToken))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        return new OfferDetails(
            (await rows.ItemsAsync([offer], cancellationToken)).Single(),
            offer.Grade,
            salaryVisible ? offer.Salary : null,
            salaryVisible ? offer.Currency : null,
            salaryVisible ? offer.Period : null,
            salaryVisible,
            requisition?.SalaryMax,
            requisition?.SalaryMax is null ? null : requisition.Currency,
            offer.Benefits,
            offer.HiringManagerUserId,
            people.GetValueOrDefault(offer.HiringManagerUserId),
            offer.RequesterUserId,
            people.GetValueOrDefault(offer.RequesterUserId),
            offer.ReturnComment,
            offer.WithdrawalReason,
            offer.SubmittedAt,
            offer.DecidedAt,
            offer.RespondedVia,
            offer.RespondedByUserId is { } by ? people.GetValueOrDefault(by) : null,
            offer.ResponseNote,
            manages && offer.Status is OfferStatus.Draft or OfferStatus.Returned,
            manages && offer.Status == OfferStatus.Approved,
            manages && offer.Status == OfferStatus.Sent,
            manages && offer.IsOpen,
            found.Version);
    }
}

internal sealed class GetOfferLetterQueryHandler(IRecruitmentDbContext db, OfferAccess access, OfferLetters letters, IFileStore files)
    : IQueryHandler<GetOfferLetterQuery, RecruitmentFileDownload>
{
    public async Task<Result<RecruitmentFileDownload>> Handle(GetOfferLetterQuery request, CancellationToken cancellationToken)
    {
        var offer = await db.JobOffers.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOfferId.From(request.Id), cancellationToken);
        if (offer is null || !((!request.AsTeam && await access.ManagesAsync(offer.CompanyId, cancellationToken)) || await access.ApprovesAsync(offer, cancellationToken)))
        {
            return CommonErrors.NotFound;
        }

        if (offer.LetterFileId is { } fileId && await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken) is { } file)
        {
            return new RecruitmentFileDownload(file.FileName, file.ContentType, file.Length, await files.OpenReadAsync(file, cancellationToken));
        }

        var printed = await letters.PrintAsync(offer, cancellationToken);
        return printed.IsFailure
            ? printed.Error
            : new RecruitmentFileDownload(printed.Value.FileName, "application/pdf", printed.Value.Content.Length, new MemoryStream(printed.Value.Content));
    }
}

internal sealed class CreateOfferCommandHandler(
    IRecruitmentDbContext db,
    OfferAccess access,
    ICurrentUser currentUser,
    INumberGenerator numbers,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : ICommandHandler<CreateOfferCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOfferCommand request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.ApplicationId), cancellationToken);
        if (application is null || !await access.ManagesAsync(application.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("applicationId", OfferErrors.UnknownApplicationCode)]);
        }

        if (await db.JobOffers.AnyAsync(o => o.ApplicationId == application.Id && OfferStates.OpenOrAccepted.Contains(o.Status), cancellationToken))
        {
            return JobOfferErrors.OpenOfferExists;
        }

        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        var orgUnitId = opening.RequisitionId is { } requisitionId
            ? await db.JobRequisitions.AsNoTracking().Where(r => r.Id == requisitionId).Select(r => (Guid?)r.OrgUnitId).SingleOrDefaultAsync(cancellationToken)
            : null;
        var today = await OfferRules.TodayAsync(organization, timeProvider, application.CompanyId, cancellationToken);
        var number = await numbers.NextAsync(application.CompanyId, RecruitmentDocumentTypes.Offer, today, cancellationToken);
        var offer = JobOffer.Draft(number, application, opening, orgUnitId, currentUser.UserId ?? Guid.Empty, OfferRules.Terms(request, opening.Title));
        if (offer.IsFailure)
        {
            return offer.Error;
        }

        db.JobOffers.Add(offer.Value);
        return offer.Value.Id.Value;
    }
}

internal sealed class UpdateOfferCommandHandler(IRecruitmentDbContext db, OfferAccess access) : ICommandHandler<UpdateOfferCommand>
{
    public async Task<Result> Handle(UpdateOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await OfferChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var title = await db.JobOpenings.AsNoTracking().Where(o => o.Id == offer.OpeningId).Select(o => o.Title).SingleAsync(cancellationToken);
        return offer.Update(OfferRules.Terms(request, title));
    }
}

internal sealed class SubmitOfferCommandHandler(
    IRecruitmentDbContext db,
    OfferAccess access,
    IOrgQuery org,
    IOrganizationLookup organization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<SubmitOfferCommand>
{
    public async Task<Result> Handle(SubmitOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await OfferChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var requisitionId = await db.JobOpenings.AsNoTracking().Where(o => o.Id == offer.OpeningId).Select(o => o.RequisitionId).SingleAsync(cancellationToken);
        var budget = requisitionId is { } id
            ? await db.JobRequisitions.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.SalaryMax, r.Currency }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var today = await OfferRules.TodayAsync(organization, timeProvider, offer.CompanyId, cancellationToken);
        var submitted = offer.Submit(offer.ExceedsBudget(budget?.SalaryMax, budget?.Currency), today, timeProvider.GetUtcNow());
        if (submitted.IsFailure)
        {
            return submitted;
        }

        var candidate = await db.Candidates.AsNoTracking().Where(c => c.Id == offer.CandidateId).Select(c => c.FirstName + " " + c.LastName).SingleAsync(cancellationToken);
        var requester = await org.GetUserOrgInfoAsync(offer.RequesterUserId, cancellationToken);
        await publisher.PublishAsync(
            new ApprovalRequested(
                RecruitmentDocumentTypes.Offer,
                offer.Id.Value.ToString(),
                offer.RequesterUserId,
                requester?.EmployeeId,
                JsonSerializer.SerializeToElement(new
                {
                    salary = OfferRules.Monthly(offer),
                    grade = offer.Grade,
                    companyId = offer.CompanyId,
                    orgUnitId = offer.OrgUnitId,
                    hiringManagerUserId = offer.HiringManagerUserId,
                    isAboveBudget = offer.IsAboveBudget,
                }),
                "recruitment.offers.approval_title",
                new Dictionary<string, string> { ["number"] = offer.Number, ["candidate"] = candidate, ["position"] = offer.PositionTitle },
                $"{candidate} · {offer.PositionTitle}",
                $"/recruitment/offers/{offer.Id.Value}")
            {
                CompanyId = offer.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class SendOfferCommandHandler(
    IRecruitmentDbContext db,
    OfferAccess access,
    OfferLetters letters,
    IFileStore files,
    IOrganizationLookup organization,
    ITenantContext tenant,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<SendOfferCommand>
{
    public const string OfferFileOwner = "recruitment.offer";

    public async Task<Result> Handle(SendOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await OfferChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        if (offer.Status != OfferStatus.Approved)
        {
            return JobOfferErrors.NotApproved;
        }

        var printed = await letters.PrintAsync(offer, cancellationToken);
        if (printed.IsFailure)
        {
            return printed.Error;
        }

        var (content, fileName, culture) = printed.Value;
        var stored = await files.SaveAsync(OfferFileOwner, offer.Id.Value, StoredFilePurposes.Attachment, new FileUpload(fileName, new MemoryStream(content)), OfferLetters.Rules, cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        db.StoredFiles.Add(stored.Value);
        var (token, hash) = OfferRules.NewToken();
        var company = (await organization.GetCompaniesAsync([offer.CompanyId], cancellationToken)).SingleOrDefault();
        var today = RecruitmentClock.Today(timeProvider, company?.TimeZoneId);
        var sent = offer.Send(stored.Value.Id, hash, today, timeProvider.GetUtcNow());
        if (sent.IsFailure)
        {
            return sent;
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == offer.CandidateId, cancellationToken);
        var format = CultureInfo.GetCultureInfo(culture);
        await publisher.PublishAsync(
            new ExternalEmailRequested(
                RecruitmentNotificationTypes.OfferSent,
                candidate.Email,
                candidate.FullName,
                culture,
                new Dictionary<string, string>
                {
                    ["candidate"] = candidate.FullName,
                    ["position"] = offer.PositionTitle,
                    ["company"] = company?.ShortName ?? string.Empty,
                    ["start_date"] = offer.StartDate.ToString("d", format),
                    ["valid_until"] = offer.ExpiresOn.ToString("d", format),
                })
            {
                CompanyId = offer.CompanyId,
                Attachments = [new EmailFile(fileName, "application/pdf", content)],
                Link = $"/careers/{tenant.RequiredTenant.Code}/offers/{token}",
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class RecordOfferResponseCommandHandler(
    IRecruitmentDbContext db,
    OfferAccess access,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : ICommandHandler<RecordOfferResponseCommand>
{
    public async Task<Result> Handle(RecordOfferResponseCommand request, CancellationToken cancellationToken)
    {
        var offer = await OfferChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var today = await OfferRules.TodayAsync(organization, timeProvider, offer.CompanyId, cancellationToken);
        return offer.Respond(request.Accepted, request.Note, OfferResponseChannel.Hr, currentUser.UserId, today, timeProvider.GetUtcNow());
    }
}

internal sealed class WithdrawOfferCommandHandler(IRecruitmentDbContext db, OfferAccess access, IIntegrationEventPublisher publisher) : ICommandHandler<WithdrawOfferCommand>
{
    public async Task<Result> Handle(WithdrawOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await OfferChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var withdrawn = offer.Withdraw(request.Reason);
        if (withdrawn.IsFailure)
        {
            return withdrawn.Error;
        }

        if (withdrawn.Value)
        {
            await publisher.PublishAsync(new ApprovalCancellationRequested(RecruitmentDocumentTypes.Offer, offer.Id.Value.ToString()) { CompanyId = offer.CompanyId }, cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class GetOfferResponsePageQueryHandler(IRecruitmentDbContext db, IOrganizationLookup organization, TimeProvider timeProvider)
    : IQueryHandler<GetOfferResponsePageQuery, OfferResponsePage>
{
    public async Task<Result<OfferResponsePage>> Handle(GetOfferResponsePageQuery request, CancellationToken cancellationToken)
    {
        var hash = OfferRules.Hash(request.Token);
        var offer = await db.JobOffers.AsNoTracking().SingleOrDefaultAsync(o => o.ResponseTokenHash == hash, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var company = (await organization.GetCompaniesAsync([offer.CompanyId], cancellationToken)).SingleOrDefault();
        var candidate = await db.Candidates.AsNoTracking().Where(c => c.Id == offer.CandidateId).Select(c => c.FirstName + " " + c.LastName).SingleAsync(cancellationToken);
        var today = RecruitmentClock.Today(timeProvider, company?.TimeZoneId);
        return new OfferResponsePage(
            company?.ShortName ?? string.Empty,
            candidate,
            offer.PositionTitle,
            offer.StartDate,
            offer.Salary,
            offer.Currency,
            offer.Period,
            offer.Benefits,
            offer.ExpiresOn,
            offer.Status == OfferStatus.Sent && offer.ExpiresOn < today ? OfferStatus.Expired : offer.Status,
            offer.Status == OfferStatus.Sent && offer.ExpiresOn >= today);
    }
}

internal sealed class RespondToOfferCommandHandler(
    IRecruitmentDbContext db,
    IOrganizationLookup organization,
    IIntegrationEventPublisher publisher,
    ILocalizationSource localization,
    ITextLocalizer localizer,
    TimeProvider timeProvider) : ICommandHandler<RespondToOfferCommand>
{
    public async Task<Result> Handle(RespondToOfferCommand request, CancellationToken cancellationToken)
    {
        var hash = OfferRules.Hash(request.Token);
        var offer = await db.JobOffers.SingleOrDefaultAsync(o => o.ResponseTokenHash == hash, cancellationToken);
        if (offer is null)
        {
            return CommonErrors.NotFound;
        }

        var today = await OfferRules.TodayAsync(organization, timeProvider, offer.CompanyId, cancellationToken);
        var answered = offer.Respond(request.Accepted, request.Note, OfferResponseChannel.Link, null, today, timeProvider.GetUtcNow());
        if (answered.IsFailure)
        {
            return answered;
        }

        var candidate = await db.Candidates.AsNoTracking().Where(c => c.Id == offer.CandidateId).Select(c => c.FirstName + " " + c.LastName).SingleAsync(cancellationToken);
        await publisher.PublishAsync(
            new NotificationRequested(
                OfferNotificationTypes.Answered,
                [.. new[] { offer.RequesterUserId, offer.HiringManagerUserId }.Distinct()],
                new Dictionary<string, string>
                {
                    ["number"] = offer.Number,
                    ["candidate"] = candidate,
                    ["position"] = offer.PositionTitle,
                    ["answer"] = localizer.Get($"recruitment.offers.status.{offer.Status}", culture: (await localization.GetAsync(cancellationToken)).DefaultCulture),
                },
                $"/recruitment/offers/{offer.Id.Value}")
            {
                CompanyId = offer.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class CompleteOfferCommandHandler(IRecruitmentDbContext db, IIntegrationEventPublisher publisher, ILocalizationSource localization, ITextLocalizer localizer)
    : ICommandHandler<CompleteOfferCommand>
{
    public async Task<Result> Handle(CompleteOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await db.JobOffers.SingleOrDefaultAsync(o => o.Id == JobOfferId.From(request.OfferId), cancellationToken);
        if (offer is not { Status: OfferStatus.Pending })
        {
            return Result.Success();
        }

        offer.Complete(request.Outcome == ApprovalOutcome.Approved, request.DecidedAt);
        await publisher.PublishAsync(
            new NotificationRequested(
                OfferNotificationTypes.Completed,
                [offer.RequesterUserId],
                new Dictionary<string, string> { ["number"] = offer.Number, ["position"] = offer.PositionTitle, ["result"] = localizer.Get($"recruitment.offers.status.{offer.Status}", culture: (await localization.GetAsync(cancellationToken)).DefaultCulture) },
                $"/recruitment/offers/{offer.Id.Value}")
            {
                CompanyId = offer.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReturnOfferCommandHandler(IRecruitmentDbContext db, IIntegrationEventPublisher publisher) : ICommandHandler<ReturnOfferCommand>
{
    public async Task<Result> Handle(ReturnOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await db.JobOffers.SingleOrDefaultAsync(o => o.Id == JobOfferId.From(request.OfferId), cancellationToken);
        if (offer is not { Status: OfferStatus.Pending })
        {
            return Result.Success();
        }

        offer.Return(request.Comment);
        await publisher.PublishAsync(
            new NotificationRequested(
                OfferNotificationTypes.Returned,
                [offer.RequesterUserId],
                new Dictionary<string, string> { ["number"] = offer.Number, ["comment"] = offer.ReturnComment ?? string.Empty },
                $"/recruitment/offers/{offer.Id.Value}")
            {
                CompanyId = offer.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }
}

internal sealed class ExpireOffersCommandHandler(IRecruitmentDbContext db, IOrganizationLookup organization, TimeProvider timeProvider)
    : ICommandHandler<ExpireOffersCommand, int>
{
    public async Task<Result<int>> Handle(ExpireOffersCommand request, CancellationToken cancellationToken)
    {
        // A day ahead of UTC covers every time zone; each company's own day decides
        var latest = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var due = await db.JobOffers.Where(o => o.Status == OfferStatus.Sent && o.ExpiresOn < latest.AddDays(1)).ToListAsync(cancellationToken);
        var zones = (await organization.GetCompaniesAsync([.. due.Select(o => o.CompanyId).Distinct()], cancellationToken)).ToDictionary(c => c.Id, c => c.TimeZoneId);
        return due.Count(o => o.Expire(RecruitmentClock.Today(timeProvider, zones.GetValueOrDefault(o.CompanyId))));
    }
}

/// <summary>Inbox: the approval of an offer ended.</summary>
internal sealed class OfferApprovalCompletedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestCompleted>
{
    public async Task Handle(ApprovalRequestCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType == RecruitmentDocumentTypes.Offer && Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            await RequisitionInbox.SendAsync(sender, new CompleteOfferCommand(id, integrationEvent.Outcome, integrationEvent.DecidedAt), cancellationToken);
        }
    }
}

/// <summary>Inbox: an approver returned an offer.</summary>
internal sealed class OfferApprovalReturnedHandler(ISender sender) : IIntegrationEventHandler<ApprovalRequestReturned>
{
    public async Task Handle(ApprovalRequestReturned integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DocumentType == RecruitmentDocumentTypes.Offer && Guid.TryParse(integrationEvent.DocumentId, out var id))
        {
            await RequisitionInbox.SendAsync(sender, new ReturnOfferCommand(id, integrationEvent.Comment), cancellationToken);
        }
    }
}

internal static class OfferStates
{
    /// <summary>An application has at most one offer in these states (D53).</summary>
    public static readonly OfferStatus[] OpenOrAccepted =
        [OfferStatus.Draft, OfferStatus.Pending, OfferStatus.Returned, OfferStatus.Approved, OfferStatus.Sent, OfferStatus.Accepted];
}
