using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Privacy;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Candidates;

/// <summary>A tag with its name in the user's language.</summary>
public sealed record CandidateTag(string Code, string Name);

/// <summary>A candidate of the pool in a list.</summary>
public sealed record CandidateListItem(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string? CurrentTitle,
    string? CurrentCompany,
    string? Location,
    string Source,
    IReadOnlyList<CandidateTag> Tags,
    int ApplicationCount,
    DateTimeOffset? LastAppliedAt,
    DateTimeOffset CreatedAt);

/// <summary>A CV of the candidate; the latest one is searched and sent with new applications.</summary>
public sealed record CandidateFileItem(Guid Id, string FileName, string ContentType, long Length, DateTimeOffset UploadedAt, bool IsLatest);

/// <summary>A user named in a note.</summary>
public sealed record CandidateNoteUser(Guid UserId, string? Name);

/// <summary>A note of the team; <c>CanRemove</c> for its author.</summary>
public sealed record CandidateNoteItem(Guid Id, string Body, Guid? AuthorUserId, string? AuthorName, IReadOnlyList<CandidateNoteUser> Mentioned, DateTimeOffset At, bool CanRemove);

/// <summary>An application of the candidate with the KVKK texts accepted on its form (null when HR added it).</summary>
public sealed record CandidateApplicationItem(ApplicationListItem Application, ApplicationConsent? Consent);

/// <summary>Another candidate who may be the same person (D50): same phone, or same first and last name.</summary>
public sealed record CandidateDuplicate(Guid Id, string FullName, string Email, string? Phone, bool SamePhone, bool SameName);

/// <summary>What happened to a candidate, newest first: an application, a stage change, a note or a CV.</summary>
public sealed record CandidateTimelineItem(
    DateTimeOffset At,
    CandidateTimelineKind Kind,
    string? OpeningTitle,
    string? FromStageName,
    string? ToStageName,
    ApplicationStatus? Status,
    string? Text,
    string? UserName);

[JsonConverter(typeof(JsonStringEnumConverter<CandidateTimelineKind>))]
public enum CandidateTimelineKind
{
    Applied,
    StageChanged,
    Note,
    Cv,
}

/// <summary>How many forms gave an advice.</summary>
public sealed record RecommendationCount(Recommendation Recommendation, int Count);

/// <summary>A criterion's average score across the forms of an application.</summary>
public sealed record CriterionAverage(string Criterion, decimal Average, int Count);

/// <summary>The interview forms of an application in short (D52): the weighted score, the advice and the criteria.</summary>
public sealed record CandidateEvaluation(
    Guid ApplicationId,
    string OpeningTitle,
    decimal? Score,
    int FeedbackCount,
    IReadOnlyList<RecommendationCount> Recommendations,
    IReadOnlyList<CriterionAverage> Criteria);

/// <summary>A candidate's card (D50): profile, tags, consent, applications, CVs, notes, possible duplicates, timeline, and the interview evaluations (D52).</summary>
/// <remarks>The hiring team sees the applications to its openings and no CV versions; <c>CanManage</c> is false for it.</remarks>
public sealed record CandidateDetails(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? Culture,
    string Source,
    string? SourceName,
    Guid? ReferrerEmployeeId,
    string? ReferrerName,
    bool IsEmployee,
    CandidateProfile Profile,
    IReadOnlyList<CandidateTag> Tags,
    int? PoolConsentRevision,
    DateTimeOffset? PoolConsentAt,
    IReadOnlyList<CandidateApplicationItem> Applications,
    IReadOnlyList<CandidateFileItem> Files,
    IReadOnlyList<CandidateNoteItem> Notes,
    IReadOnlyList<CandidateDuplicate> Duplicates,
    IReadOnlyList<CandidateTimelineItem> Timeline,
    bool CanManage,
    bool CanAddToOpening,
    uint Version,
    IReadOnlyList<CandidateEvaluation> Evaluations,
    IReadOnlyList<CandidateConsentItem> Consents,
    DateTimeOffset? AnonymizedAt,
    DateOnly? RetentionEndsOn,
    bool CanManagePrivacy);

/// <summary>What the candidate form gives.</summary>
public interface ICandidateFields
{
    string? Email { get; }

    string? FirstName { get; }

    string? LastName { get; }

    string? Phone { get; }

    string? Source { get; }

    Guid? ReferrerEmployeeId { get; }

    string? Location { get; }

    string? CurrentTitle { get; }

    string? CurrentCompany { get; }

    decimal? ExperienceYears { get; }

    string? Education { get; }

    string? LinkedInUrl { get; }

    string? PortfolioUrl { get; }

    IReadOnlyList<string>? Tags { get; }
}

/// <summary>
/// The pool search in the database (D50): full text with the Turkish dictionary or parts of names, and the jsonb tag
/// filter. Infrastructure implements it so the application layer does not depend on the provider.
/// </summary>
public interface ICandidateSearch
{
    IQueryable<Candidate> Matching(IQueryable<Candidate> candidates, string text);

    IQueryable<Candidate> WithTag(IQueryable<Candidate> candidates, string code);
}

/// <summary>The candidates of the pool, newest first unless sorted; the search covers profile, tags, notes and CV text.</summary>
public sealed record GetCandidatesQuery(PageRequest Paging, string? Search = null, string? Tag = null, string? Source = null)
    : IQuery<PagedResult<CandidateListItem>>, IRequirePermission
{
    public const string DefaultSort = "-createdAt";

    internal static readonly SortMap<Candidate> Sorts = SortMap<Candidate>.By(c => c.Id)
        .Add("createdAt", c => c.CreatedAt)
        .Add("lastAppliedAt", c => c.LastAppliedAt)
        .Add("name", c => c.FirstName);

    public string Permission => RecruitmentPermissions.Candidates.Read;
}

/// <summary>A candidate's card; from self service the hiring team sees the candidates of its openings.</summary>
public sealed record GetCandidateQuery(Guid Id) : IQuery<CandidateDetails>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Candidates.Read;
}

/// <summary>A CV of the candidate.</summary>
public sealed record GetCandidateFileQuery(Guid Id, Guid FileId) : IQuery<RecruitmentFileDownload>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Read;
}

/// <summary>The active tags and candidate sources to choose from (D47 lists).</summary>
public sealed record CandidateOptions(IReadOnlyList<CandidateTag> Tags, IReadOnlyList<CandidateTag> Sources);

/// <summary>The active tags and candidate sources, for the pool filters and the candidate form.</summary>
public sealed record GetCandidateOptionsQuery : IQuery<CandidateOptions>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Read;
}

/// <summary>Employees of a company in scope by name or registry number, to pick the referrer (at most 20).</summary>
public sealed record SearchCandidateReferrersQuery(Guid CompanyId, string? Search = null) : IQuery<IReadOnlyList<EmployeeSummary>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Active users by name to mention in a note (at most 20).</summary>
public sealed record SearchMentionUsersQuery(string? Search = null) : IQuery<IReadOnlyList<RequisitionUserOption>>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Candidates.Read;
}

/// <summary>Adds a candidate to the pool; returns the id. The e-mail of another candidate is refused (D50).</summary>
public sealed record CreateCandidateCommand(
    string? Email,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Source,
    Guid? ReferrerEmployeeId,
    string? Location,
    string? CurrentTitle,
    string? CurrentCompany,
    decimal? ExperienceYears,
    string? Education,
    string? LinkedInUrl,
    string? PortfolioUrl,
    IReadOnlyList<string>? Tags) : ICommand<Guid>, IRequirePermission, ICandidateFields
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Corrects a candidate's contact details, profile, source and tags.</summary>
public sealed record UpdateCandidateCommand(
    uint Version,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Source,
    Guid? ReferrerEmployeeId,
    string? Location,
    string? CurrentTitle,
    string? CurrentCompany,
    decimal? ExperienceYears,
    string? Education,
    string? LinkedInUrl,
    string? PortfolioUrl,
    IReadOnlyList<string>? Tags) : ICommand, IRequirePermission, ICandidateFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Uploads a newer CV (PDF or DOCX, 5 MB); returns the file id. Its text is searched once it is read.</summary>
public sealed record UploadCandidateCvCommand(Guid Id, FileUpload? Cv) : ICommand<Guid>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Adds a note; the users mentioned are notified. From self service the hiring team notes its candidates.</summary>
public sealed record AddCandidateNoteCommand(string? Body, IReadOnlyList<Guid>? MentionedUserIds) : ICommand<Guid>, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public bool AsTeam { get; init; }

    [JsonIgnore]
    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Removes a note; only its author does.</summary>
public sealed record RemoveCandidateNoteCommand(Guid Id, Guid NoteId) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Takes a duplicate into this candidate (D50): its applications, CVs and notes move; it is removed.</summary>
public sealed record MergeCandidatesCommand(uint Version, Guid DuplicateId) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

/// <summary>Adds the candidate to another opening (published or on hold) of a company in scope; returns the application.</summary>
public sealed record AddCandidateToOpeningCommand(Guid OpeningId) : ICommand<Guid>, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Reads the text of the newer CVs for the search (the CV text job, D50); returns how many.</summary>
public sealed record IndexCandidateCvsCommand : ICommand<int>, IBulkCommand;

public static class CandidateFieldErrors
{
    public const string EmailTakenCode = "recruitment.candidates.errors.email_taken";
    public const string UnknownTagCode = "recruitment.candidates.errors.unknown_tag";
    public const string UnknownSourceCode = "recruitment.candidates.errors.unknown_source";
    public const string UnknownReferrerCode = "recruitment.candidates.errors.unknown_referrer";
    public const string InvalidUrlCode = "recruitment.candidates.errors.invalid_url";

    public static readonly Error BothApplied = Error.Validation([new FieldError("duplicateId", "recruitment.candidates.errors.both_applied")]);
    public static readonly Error AlreadyInOpening = Error.Validation([new FieldError("openingId", "recruitment.candidates.errors.already_in_opening")]);
    public static readonly Error UnknownOpening = Error.Validation([new FieldError("openingId", "recruitment.candidates.errors.unknown_opening")]);
}

/// <summary>Mentions in candidate notes (D50).</summary>
public sealed class CandidateNotificationTypes : INotificationTypeProvider
{
    public const string Mentioned = "recruitment.candidate_mentioned";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Mentioned, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>Checks and builds what the candidate form gives: an active source, active tags, a known referrer.</summary>
public sealed class CandidateRules(IRecruitmentDbContext db, IEmployeeLookup employees)
{
    public const string CandidateFileOwner = ApplicationIntake.CandidateFileOwner;

    public async Task<Result<(CandidateContact Contact, CandidateProfile Profile, List<string> Tags, string? TagText)>> CheckAsync(
        ICandidateFields fields,
        string? currentSource,
        CancellationToken cancellationToken)
    {
        var source = fields.Source!.Trim().ToUpperInvariant();
        if (source != currentSource
            && !await db.Lookups.AnyAsync(l => l.Kind == LookupKind.CandidateSource && l.Code == source && l.IsActive, cancellationToken))
        {
            return Field("source", CandidateFieldErrors.UnknownSourceCode);
        }

        var codes = (fields.Tags ?? []).Select(t => t.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var tags = await db.Lookups.AsNoTracking().Where(l => l.Kind == LookupKind.Tag && codes.Contains(l.Code)).ToListAsync(cancellationToken);
        if (tags.Count != codes.Count)
        {
            return Field("tags", CandidateFieldErrors.UnknownTagCode);
        }

        if (fields.ReferrerEmployeeId is { } referrer && await employees.GetContactAsync(referrer, cancellationToken) is null)
        {
            return Field("referrerEmployeeId", CandidateFieldErrors.UnknownReferrerCode);
        }

        return (
            new CandidateContact(fields.FirstName!, fields.LastName!, fields.Phone, null),
            new CandidateProfile(fields.Location, fields.CurrentTitle, fields.CurrentCompany, fields.ExperienceYears, fields.Education, fields.LinkedInUrl, fields.PortfolioUrl),
            codes,
            TagText(tags));
    }

    /// <summary>The source code as stored.</summary>
    public static string Source(ICandidateFields fields) => fields.Source!.Trim().ToUpperInvariant();

    /// <summary>The names of tags in every language, for the search.</summary>
    public static string? TagText(IEnumerable<RecruitmentLookup> tags) =>
        string.Join(' ', tags.SelectMany(t => t.Name.Values.Values).Distinct(StringComparer.Ordinal)) is { Length: > 0 } text ? text : null;

    public static async Task<string?> TagTextAsync(IRecruitmentDbContext db, IReadOnlyList<string> codes, CancellationToken cancellationToken) =>
        TagText(await db.Lookups.AsNoTracking().Where(l => l.Kind == LookupKind.Tag && codes.Contains(l.Code)).ToListAsync(cancellationToken));

    /// <summary>Another candidate has the e-mail (D50); the form finds that candidate in the pool and offers it.</summary>
    public static async Task<Error?> EmailTakenAsync(IRecruitmentDbContext db, string email, CandidateId? except, CancellationToken cancellationToken)
    {
        var normalized = Candidate.NormalizeEmail(email);
        return await db.Candidates.AnyAsync(c => c.Email == normalized && (except == null || c.Id != except), cancellationToken)
            ? Error.Validation([new FieldError("email", CandidateFieldErrors.EmailTakenCode)])
            : null;
    }

    private static Error Field(string field, string code) => Error.Validation([new FieldError(field, code)]);

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : ICandidateFields
    {
        validator.RuleFor(c => c.Email).NotEmpty().MaximumLength(Candidate.EmailMaxLength).EmailAddress();
        validator.RuleFor(c => c.FirstName).NotEmpty().MaximumLength(Candidate.NameMaxLength);
        validator.RuleFor(c => c.LastName).NotEmpty().MaximumLength(Candidate.NameMaxLength);
        validator.RuleFor(c => c.Phone).MaximumLength(Candidate.PhoneMaxLength);
        validator.RuleFor(c => c.Source).NotEmpty().MaximumLength(Candidate.SourceMaxLength);
        validator.RuleFor(c => c.Location).MaximumLength(CandidateProfile.TextMaxLength);
        validator.RuleFor(c => c.CurrentTitle).MaximumLength(CandidateProfile.TextMaxLength);
        validator.RuleFor(c => c.CurrentCompany).MaximumLength(CandidateProfile.TextMaxLength);
        validator.RuleFor(c => c.ExperienceYears).InclusiveBetween(0, CandidateProfile.MaxExperienceYears).When(c => c.ExperienceYears is not null);
        validator.RuleFor(c => c.Education).MaximumLength(CandidateProfile.EducationMaxLength);
        validator.RuleFor(c => c.LinkedInUrl).MaximumLength(CandidateProfile.UrlMaxLength)
            .Must(IsWebUrl).WithErrorCode(CandidateFieldErrors.InvalidUrlCode).When(c => !string.IsNullOrWhiteSpace(c.LinkedInUrl));
        validator.RuleFor(c => c.PortfolioUrl).MaximumLength(CandidateProfile.UrlMaxLength)
            .Must(IsWebUrl).WithErrorCode(CandidateFieldErrors.InvalidUrlCode).When(c => !string.IsNullOrWhiteSpace(c.PortfolioUrl));
        validator.RuleFor(c => c.Tags).Must(t => t is null || t.Count <= Candidate.MaxTags).WithErrorCode(CommonErrors.ValidationCode);
    }

    private static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

internal sealed class CreateCandidateCommandValidator : AbstractValidator<CreateCandidateCommand>
{
    public CreateCandidateCommandValidator() => CandidateRules.AddFieldRules(this);
}

internal sealed class UpdateCandidateCommandValidator : AbstractValidator<UpdateCandidateCommand>
{
    public UpdateCandidateCommandValidator() => CandidateRules.AddFieldRules(this);
}

internal sealed class AddCandidateNoteCommandValidator : AbstractValidator<AddCandidateNoteCommand>
{
    public AddCandidateNoteCommandValidator()
    {
        RuleFor(c => c.Body).NotEmpty().MaximumLength(CandidateNote.BodyMaxLength);
        RuleFor(c => c.MentionedUserIds).Must(m => m is null || m.Count <= 20).WithErrorCode(CommonErrors.ValidationCode);
    }
}

internal sealed class UploadCandidateCvCommandValidator : AbstractValidator<UploadCandidateCvCommand>
{
    public UploadCandidateCvCommandValidator() => RuleFor(c => c.Cv).NotNull();
}

internal sealed class GetCandidatesQueryValidator : AbstractValidator<GetCandidatesQuery>
{
    public GetCandidatesQueryValidator() =>
        RuleFor(q => q.Paging.Sort).Must(GetCandidatesQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
}

internal sealed class GetCandidatesQueryHandler(IRecruitmentDbContext db, ICandidateSearch search, ICurrentUser currentUser)
    : IQueryHandler<GetCandidatesQuery, PagedResult<CandidateListItem>>
{
    public async Task<Result<PagedResult<CandidateListItem>>> Handle(GetCandidatesQuery request, CancellationToken cancellationToken)
    {
        // The pool is the tenant's (PLAN R4); the applications on a card follow the company scope
        var candidates = db.Candidates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            candidates = search.Matching(candidates, request.Search.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tag = request.Tag.Trim().ToUpperInvariant();
            candidates = search.WithTag(candidates, tag);
        }

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            var source = request.Source.Trim().ToUpperInvariant();
            candidates = candidates.Where(c => c.Source == source);
        }

        var page = await GetCandidatesQuery.Sorts.Apply(candidates, request.Paging.Sort, GetCandidatesQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        var ids = page.Items.Select(c => c.Id).ToList();
        var counts = await db.JobApplications.AsNoTracking()
            .Where(a => ids.Contains(a.CandidateId))
            .GroupBy(a => a.CandidateId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var tags = await CandidateTags.NamesAsync(db, page.Items.SelectMany(c => c.Tags), currentUser.Culture, cancellationToken);
        return new PagedResult<CandidateListItem>(
            [
                .. page.Items.Select(c => new CandidateListItem(
                    c.Id.Value,
                    c.FullName,
                    c.Email,
                    c.Phone,
                    c.CurrentTitle,
                    c.CurrentCompany,
                    c.Location,
                    c.Source,
                    CandidateTags.Of(c.Tags, tags),
                    counts.GetValueOrDefault(c.Id),
                    c.LastAppliedAt,
                    c.CreatedAt)),
            ],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal static class CandidateTags
{
    public static async Task<Dictionary<string, string>> NamesAsync(IRecruitmentDbContext db, IEnumerable<string> codes, string culture, CancellationToken cancellationToken)
    {
        var distinct = codes.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 0
            ? []
            : (await db.Lookups.AsNoTracking().Where(l => l.Kind == LookupKind.Tag && distinct.Contains(l.Code)).ToListAsync(cancellationToken))
                .ToDictionary(l => l.Code, l => l.Name.Get(culture, "tr-TR"), StringComparer.Ordinal);
    }

    public static List<CandidateTag> Of(IEnumerable<string> codes, Dictionary<string, string> names) =>
        [.. codes.Select(code => new CandidateTag(code, names.GetValueOrDefault(code) ?? code))];
}

internal sealed class GetCandidateQueryHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    ApplicationAccess access,
    ApplicationRows rows,
    IUserDirectory users,
    IEmployeeLookup employees,
    ISettings settings,
    ICurrentUser currentUser) : IQueryHandler<GetCandidateQuery, CandidateDetails>
{
    public async Task<Result<CandidateDetails>> Handle(GetCandidateQuery request, CancellationToken cancellationToken)
    {
        var id = CandidateId.From(request.Id);
        var candidate = await db.Candidates.AsNoTracking().Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        // The applications the caller reaches: of the companies in scope, or of the team's openings
        var query = rows.Query().Where(r => r.CandidateId == id);
        if (request.AsTeam)
        {
            var team = (await access.TeamOpeningsAsync(cancellationToken)).Select(o => o.Id).ToList();
            query = query.Where(r => team.Contains(r.OpeningId));
        }
        else if (await RecruitmentSettingRules.CompaniesAsync(scopes, RecruitmentPermissions.Applications.Read, cancellationToken) is { } companies)
        {
            var ids = companies.ToList();
            query = query.Where(r => ids.Contains(r.CompanyId));
        }

        var applicationRows = await query.OrderByDescending(r => r.AppliedAt).ToListAsync(cancellationToken);
        if (request.AsTeam && applicationRows.Count == 0)
        {
            return CommonErrors.NotFound;
        }

        var culture = currentUser.Culture;
        var applicationIds = applicationRows.Select(r => r.Id).ToList();
        var applications = await db.JobApplications.AsNoTracking().Include(a => a.History)
            .Where(a => applicationIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
        var openingIds = applicationRows.Select(r => r.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var files = request.AsTeam
            ? []
            : await db.StoredFiles.AsNoTracking()
                .Where(f => f.OwnerType == CandidateRules.CandidateFileOwner && f.OwnerId == id.Value)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync(cancellationToken);
        var userIds = candidate.Notes.SelectMany(n => n.MentionedUserIds.Append(n.AuthorUserId ?? Guid.Empty))
            .Concat(applications.Values.SelectMany(a => a.History).Select(h => h.UserId ?? Guid.Empty))
            .Where(u => u != Guid.Empty)
            .Distinct()
            .ToList();
        var names = (await users.GetAsync(userIds, cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
        string? Name(Guid? userId) => userId is { } value ? names.GetValueOrDefault(value) : null;
        var tags = await CandidateTags.NamesAsync(db, candidate.Tags, culture, cancellationToken);
        var source = await db.Lookups.AsNoTracking().Where(l => l.Kind == LookupKind.CandidateSource && l.Code == candidate.Source).SingleOrDefaultAsync(cancellationToken);
        var referrer = candidate.ReferrerEmployeeId is { } referrerId ? await employees.GetContactAsync(referrerId, cancellationToken) : null;
        var manages = !request.AsTeam && (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Candidates.Manage, cancellationToken)).Count > 0;
        var addsToOpenings = !request.AsTeam && (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Applications.Manage, cancellationToken)).Count > 0;

        var timeline = new List<CandidateTimelineItem>();
        foreach (var row in applicationRows)
        {
            var opening = openings[row.OpeningId];
            string? Stage(Guid? stageId) => opening.Stages.FirstOrDefault(s => s.Id == stageId)?.Name.Get(culture, "tr-TR");
            timeline.AddRange(applications[row.Id].History.Select(h => new CandidateTimelineItem(
                h.At,
                h.FromStageId is null ? CandidateTimelineKind.Applied : CandidateTimelineKind.StageChanged,
                opening.Title,
                Stage(h.FromStageId),
                Stage(h.ToStageId),
                h.Status,
                h.Note,
                Name(h.UserId))));
        }

        timeline.AddRange(candidate.Notes.Select(n => new CandidateTimelineItem(n.At, CandidateTimelineKind.Note, null, null, null, null, n.Body, Name(n.AuthorUserId))));
        timeline.AddRange(files.Select(f => new CandidateTimelineItem(f.CreatedAt, CandidateTimelineKind.Cv, null, null, null, null, f.FileName, Name(f.CreatedBy))));

        return new CandidateDetails(
            candidate.Id.Value,
            candidate.Email,
            candidate.FirstName,
            candidate.LastName,
            candidate.Phone,
            candidate.Culture,
            candidate.Source,
            source?.Name.Get(culture, "tr-TR"),
            candidate.ReferrerEmployeeId,
            referrer is null ? null : $"{referrer.FirstName} {referrer.LastName}",
            candidate.EmployeeId is not null,
            new CandidateProfile(candidate.Location, candidate.CurrentTitle, candidate.CurrentCompany, candidate.ExperienceYears, candidate.Education, candidate.LinkedInUrl, candidate.PortfolioUrl),
            CandidateTags.Of(candidate.Tags, tags),
            candidate.PoolConsentRevision,
            candidate.PoolConsentAt,
            [.. applicationRows.Select(r => new CandidateApplicationItem(rows.Item(r, openings[r.OpeningId]), applications[r.Id].Consent))],
            [.. files.Select(f => new CandidateFileItem(f.Id, f.FileName, f.ContentType, f.Length, f.CreatedAt, f.Id == candidate.CvFileId))],
            [
                .. candidate.Notes.OrderByDescending(n => n.At).Select(n => new CandidateNoteItem(
                    n.Id,
                    n.Body,
                    n.AuthorUserId,
                    Name(n.AuthorUserId),
                    [.. n.MentionedUserIds.Select(u => new CandidateNoteUser(u, Name(u)))],
                    n.At,
                    n.AuthorUserId is not null && n.AuthorUserId == currentUser.UserId)),
            ],
            request.AsTeam ? [] : await DuplicatesAsync(candidate, cancellationToken),
            [.. timeline.OrderByDescending(t => t.At)],
            manages,
            addsToOpenings,
            await db.Candidates.AsNoTracking().Where(c => c.Id == id).Select(c => EF.Property<uint>(c, ConcurrencyVersion.PropertyName)).SingleAsync(cancellationToken),
            await EvaluationsAsync(applicationRows, openings, cancellationToken),
            CandidateRetention.Consents(candidate, applicationRows.Select(r => (applications[r.Id], openings[r.OpeningId].Title))),
            candidate.AnonymizedAt,
            // The retention waits while an application is in the pipeline (D55)
            candidate.IsAnonymized || await db.JobApplications.AnyAsync(a => a.CandidateId == id && a.Status == ApplicationStatus.Active, cancellationToken)
                ? null
                : CandidateRetention.EndsOn(candidate, await CandidateRetention.MonthsAsync(settings, cancellationToken)),
            !request.AsTeam && !candidate.IsAnonymized && (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Candidates.Privacy, cancellationToken)).Count > 0);
    }

    /// <summary>The forms of the applications shown, by application: the weighted score, the advice and each criterion's average.</summary>
    private async Task<List<CandidateEvaluation>> EvaluationsAsync(
        IReadOnlyList<ApplicationRow> applicationRows,
        Dictionary<JobOpeningId, JobOpening> openings,
        CancellationToken cancellationToken)
    {
        var ids = applicationRows.Select(r => r.Id).ToList();
        var forms = await db.InterviewFeedbacks.AsNoTracking().Where(f => ids.Contains(f.ApplicationId)).ToListAsync(cancellationToken);
        if (forms.Count == 0)
        {
            return [];
        }

        var interviewIds = forms.Select(f => f.InterviewId).Distinct().ToList();
        var criteria = (await db.Interviews.AsNoTracking().Where(i => interviewIds.Contains(i.Id)).ToListAsync(cancellationToken))
            .SelectMany(i => i.Evaluation?.Criteria ?? [])
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);
        return
        [
            .. applicationRows
                .Where(r => forms.Any(f => f.ApplicationId == r.Id))
                .Select(r =>
                {
                    var own = forms.Where(f => f.ApplicationId == r.Id).ToList();
                    return new CandidateEvaluation(
                        r.Id.Value,
                        openings[r.OpeningId].Title,
                        InterviewRows.Score(own),
                        own.Count,
                        [.. own.GroupBy(f => f.Recommendation).OrderBy(g => g.Key).Select(g => new RecommendationCount(g.Key, g.Count()))],
                        [
                            .. own.SelectMany(f => f.Scores)
                                .GroupBy(s => criteria.GetValueOrDefault(s.CriterionId) ?? string.Empty)
                                .Where(g => g.Key.Length > 0)
                                .Select(g => new CriterionAverage(g.Key, Math.Round((decimal)g.Average(s => s.Score), 2), g.Count())),
                        ]);
                }),
        ];
    }

    /// <summary>Other candidates with the same phone, or the same first and last name (D50).</summary>
    private async Task<List<CandidateDuplicate>> DuplicatesAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        var phone = candidate.NormalizedPhone;

        // The contains pattern without its % is an escaped, case-insensitive equality
        var first = TextSearch.ContainsPattern(candidate.FirstName)[1..^1];
        var last = TextSearch.ContainsPattern(candidate.LastName)[1..^1];
        var others = await db.Candidates.AsNoTracking()
            .Where(c => c.Id != candidate.Id
                && ((phone != null && c.NormalizedPhone == phone) || (TextSearch.Matches(c.FirstName, first) && TextSearch.Matches(c.LastName, last))))
            .OrderBy(c => c.Email)
            .Take(10)
            .ToListAsync(cancellationToken);
        return
        [
            .. others.Select(c => new CandidateDuplicate(
                c.Id.Value,
                c.FullName,
                c.Email,
                c.Phone,
                phone is not null && c.NormalizedPhone == phone,
                string.Equals(c.FirstName, candidate.FirstName, StringComparison.OrdinalIgnoreCase) && string.Equals(c.LastName, candidate.LastName, StringComparison.OrdinalIgnoreCase))),
        ];
    }
}

internal sealed class GetCandidateFileQueryHandler(IRecruitmentDbContext db, IFileStore files) : IQueryHandler<GetCandidateFileQuery, RecruitmentFileDownload>
{
    public async Task<Result<RecruitmentFileDownload>> Handle(GetCandidateFileQuery request, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == request.FileId && f.OwnerType == CandidateRules.CandidateFileOwner && f.OwnerId == request.Id, cancellationToken);
        return file is null
            ? CommonErrors.NotFound
            : new RecruitmentFileDownload(file.FileName, file.ContentType, file.Length, await files.OpenReadAsync(file, cancellationToken));
    }
}

internal sealed class GetCandidateOptionsQueryHandler(IRecruitmentDbContext db, ICurrentUser currentUser) : IQueryHandler<GetCandidateOptionsQuery, CandidateOptions>
{
    public async Task<Result<CandidateOptions>> Handle(GetCandidateOptionsQuery request, CancellationToken cancellationToken)
    {
        var lookups = await db.Lookups.AsNoTracking()
            .Where(l => l.IsActive && (l.Kind == LookupKind.Tag || l.Kind == LookupKind.CandidateSource))
            .OrderBy(l => l.SortOrder)
            .ToListAsync(cancellationToken);
        List<CandidateTag> Of(LookupKind kind) =>
            [.. lookups.Where(l => l.Kind == kind).Select(l => new CandidateTag(l.Code, l.Name.Get(currentUser.Culture, "tr-TR")))];
        return new CandidateOptions(Of(LookupKind.Tag), Of(LookupKind.CandidateSource));
    }
}

internal sealed class SearchCandidateReferrersQueryHandler(IAccessScopeProvider scopes, IEmployeeLookup employees)
    : IQueryHandler<SearchCandidateReferrersQuery, IReadOnlyList<EmployeeSummary>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<EmployeeSummary>>> Handle(SearchCandidateReferrersQuery request, CancellationToken cancellationToken) =>
        await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken)
            ? Result.Success(await employees.SearchAsync(request.CompanyId, request.Search, MaxResults, cancellationToken))
            : CommonErrors.Forbidden;
}

internal sealed class SearchMentionUsersQueryHandler(IUserDirectory users) : IQueryHandler<SearchMentionUsersQuery, IReadOnlyList<RequisitionUserOption>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<RequisitionUserOption>>> Handle(SearchMentionUsersQuery request, CancellationToken cancellationToken) =>
        (await users.SearchAsync(request.Search, MaxResults, cancellationToken))
            .Where(u => u.IsActive)
            .Select(u => new RequisitionUserOption(u.Id, u.DisplayName, u.UserName))
            .ToList();
}

internal sealed class CreateCandidateCommandHandler(IRecruitmentDbContext db, CandidateRules rules) : ICommandHandler<CreateCandidateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCandidateCommand request, CancellationToken cancellationToken)
    {
        if (await CandidateRules.EmailTakenAsync(db, request.Email!, null, cancellationToken) is { } taken)
        {
            return taken;
        }

        var checkedFields = await rules.CheckAsync(request, null, cancellationToken);
        if (checkedFields.IsFailure)
        {
            return checkedFields.Error;
        }

        var (contact, profile, tags, tagText) = checkedFields.Value;
        var candidate = Candidate.Add(request.Email!, contact, profile, CandidateRules.Source(request), request.ReferrerEmployeeId);
        candidate.SetTags(tags, tagText);
        db.Candidates.Add(candidate);
        return candidate.Id.Value;
    }
}

internal sealed class UpdateCandidateCommandHandler(IRecruitmentDbContext db, CandidateRules rules) : ICommandHandler<UpdateCandidateCommand>
{
    public async Task<Result> Handle(UpdateCandidateCommand request, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        if (candidate.IsAnonymized)
        {
            return CandidateErrors.Anonymized;
        }

        db.Entry(candidate).ExpectVersion(request.Version);
        if (await CandidateRules.EmailTakenAsync(db, request.Email!, candidate.Id, cancellationToken) is { } taken)
        {
            return taken;
        }

        var checkedFields = await rules.CheckAsync(request, candidate.Source, cancellationToken);
        if (checkedFields.IsFailure)
        {
            return checkedFields.Error;
        }

        var (contact, profile, tags, tagText) = checkedFields.Value;
        candidate.Update(request.Email!, contact, profile, CandidateRules.Source(request), request.ReferrerEmployeeId);
        candidate.SetTags(tags, tagText);
        return Result.Success();
    }
}

internal sealed class UploadCandidateCvCommandHandler(IRecruitmentDbContext db, IFileStore files) : ICommandHandler<UploadCandidateCvCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadCandidateCvCommand request, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        if (candidate.IsAnonymized)
        {
            return CandidateErrors.Anonymized;
        }

        var stored = await files.SaveAsync(CandidateRules.CandidateFileOwner, request.Id, StoredFilePurposes.Attachment, request.Cv!, CareerCatalog.CvRules, cancellationToken);
        if (stored.IsFailure)
        {
            // File errors are on the file field; the form names it cv
            return stored.Error.Fields.Count > 0 ? Error.Validation([.. stored.Error.Fields.Select(e => e with { Field = "cv" })]) : stored.Error;
        }

        db.StoredFiles.Add(stored.Value);
        candidate.ReplaceCv(stored.Value.Id);
        return stored.Value.Id;
    }
}

/// <summary>Loads a candidate to note on: any candidate for recruiters, the candidates of its openings for the hiring team.</summary>
internal static class CandidateNotes
{
    public static async Task<Candidate?> LoadAsync(IRecruitmentDbContext db, ApplicationAccess access, Guid id, bool asTeam, CancellationToken cancellationToken)
    {
        var candidateId = CandidateId.From(id);
        if (asTeam)
        {
            var team = (await access.TeamOpeningsAsync(cancellationToken)).Select(o => o.Id).ToList();
            if (!await db.JobApplications.AnyAsync(a => a.CandidateId == candidateId && team.Contains(a.OpeningId), cancellationToken))
            {
                return null;
            }
        }

        return await db.Candidates.Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == candidateId, cancellationToken);
    }
}

internal sealed class AddCandidateNoteCommandHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    IUserDirectory users,
    ICurrentUser currentUser,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<AddCandidateNoteCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddCandidateNoteCommand request, CancellationToken cancellationToken)
    {
        var candidate = await CandidateNotes.LoadAsync(db, access, request.Id, request.AsTeam, cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        if (candidate.IsAnonymized)
        {
            return CandidateErrors.Anonymized;
        }

        // Only active users are mentioned; the author is not told about their own note
        var mentioned = (await users.GetAsync([.. (request.MentionedUserIds ?? []).Distinct()], cancellationToken))
            .Where(u => u.IsActive && u.Id != currentUser.UserId)
            .ToList();
        var note = candidate.AddNote(request.Body!, [.. mentioned.Select(u => u.Id)], currentUser.UserId, timeProvider.GetUtcNow());
        if (mentioned.Count > 0)
        {
            var author = currentUser.UserId is { } authorId ? (await users.GetAsync([authorId], cancellationToken)).SingleOrDefault()?.DisplayName : null;
            await publisher.PublishAsync(
                new NotificationRequested(
                    CandidateNotificationTypes.Mentioned,
                    [.. mentioned.Select(u => u.Id)],
                    new Dictionary<string, string> { ["author"] = author ?? string.Empty, ["candidate"] = candidate.FullName },
                    $"/recruitment/candidates/{candidate.Id.Value}"),
                cancellationToken);
        }

        return note.Id;
    }
}

internal sealed class RemoveCandidateNoteCommandHandler(IRecruitmentDbContext db, ApplicationAccess access, ICurrentUser currentUser)
    : ICommandHandler<RemoveCandidateNoteCommand>
{
    public async Task<Result> Handle(RemoveCandidateNoteCommand request, CancellationToken cancellationToken)
    {
        var candidate = await CandidateNotes.LoadAsync(db, access, request.Id, request.AsTeam, cancellationToken);
        return candidate is null ? CommonErrors.NotFound : candidate.RemoveNote(request.NoteId, currentUser.UserId);
    }
}

internal sealed class MergeCandidatesCommandHandler(IRecruitmentDbContext db) : ICommandHandler<MergeCandidatesCommand>
{
    public async Task<Result> Handle(MergeCandidatesCommand request, CancellationToken cancellationToken)
    {
        var target = await db.Candidates.Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(target).ExpectVersion(request.Version);
        var duplicate = await db.Candidates.Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.DuplicateId), cancellationToken);
        if (duplicate is null)
        {
            return Error.Validation([new FieldError("duplicateId", CommonErrors.NotFound.Code)]);
        }

        if (target.IsAnonymized || duplicate.IsAnonymized)
        {
            return CandidateErrors.Anonymized;
        }

        // One application per candidate and opening: both in the same opening cannot become one candidate
        var targetOpenings = db.JobApplications.Where(a => a.CandidateId == target.Id).Select(a => a.OpeningId);
        if (await db.JobApplications.AnyAsync(a => a.CandidateId == duplicate.Id && targetOpenings.Contains(a.OpeningId), cancellationToken))
        {
            return CandidateFieldErrors.BothApplied;
        }

        var tags = target.Tags.Concat(duplicate.Tags).Distinct(StringComparer.Ordinal).Take(Candidate.MaxTags).ToList();
        return target.Absorb(duplicate, await CandidateRules.TagTextAsync(db, tags, cancellationToken));
    }
}

/// <summary>A duplicate was merged: its applications and CVs move to the candidate it was merged into, then it goes (rule 7).</summary>
internal sealed class MoveMergedCandidate(IRecruitmentDbContext db) : IDomainEventHandler<CandidateMerged>
{
    public async Task Handle(CandidateMerged domainEvent, CancellationToken cancellationToken)
    {
        foreach (var application in await db.JobApplications.Where(a => a.CandidateId == domainEvent.DuplicateId).ToListAsync(cancellationToken))
        {
            application.MoveToCandidate(domainEvent.TargetId);
        }

        var files = await db.StoredFiles
            .Where(f => f.OwnerType == CandidateRules.CandidateFileOwner && f.OwnerId == domainEvent.DuplicateId.Value)
            .ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            file.MoveTo(domainEvent.TargetId.Value);
        }

        var duplicate = await db.Candidates.SingleAsync(c => c.Id == domainEvent.DuplicateId, cancellationToken);
        db.Candidates.Remove(duplicate);
    }
}

internal sealed class AddCandidateToOpeningCommandHandler(IRecruitmentDbContext db, ApplicationAccess access, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<AddCandidateToOpeningCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddCandidateToOpeningCommand request, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        if (candidate.IsAnonymized)
        {
            return CandidateErrors.Anonymized;
        }

        var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.OpeningId), cancellationToken);
        if (opening is null || !await access.ManagesAsync(opening.CompanyId, cancellationToken))
        {
            return CandidateFieldErrors.UnknownOpening;
        }

        if (await db.JobApplications.AnyAsync(a => a.CandidateId == candidate.Id && a.OpeningId == opening.Id, cancellationToken))
        {
            return CandidateFieldErrors.AlreadyInOpening;
        }

        var application = JobApplication.AddFromPool(opening, candidate, currentUser.UserId, timeProvider.GetUtcNow());
        if (application.IsFailure)
        {
            return application.Error;
        }

        db.JobApplications.Add(application.Value);
        return application.Value.Id.Value;
    }
}

internal sealed class IndexCandidateCvsCommandHandler(IRecruitmentDbContext db, IFileStore files, IDocumentTextExtractor extractor)
    : ICommandHandler<IndexCandidateCvsCommand, int>
{
    private const int BatchSize = 50;

    public async Task<Result<int>> Handle(IndexCandidateCvsCommand request, CancellationToken cancellationToken)
    {
        var candidates = await db.Candidates
            .Where(c => c.CvFileId != null && (c.CvTextFileId == null || c.CvTextFileId != c.CvFileId))
            .OrderBy(c => c.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var fileIds = candidates.Select(c => c.CvFileId!.Value).ToList();
        var stored = await db.StoredFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        foreach (var candidate in candidates)
        {
            var fileId = candidate.CvFileId!.Value;
            string? text = null;
            if (stored.TryGetValue(fileId, out var file))
            {
                await using var content = await files.OpenReadAsync(file, cancellationToken);
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                var extracted = extractor.Extract(buffer.ToArray(), cancellationToken);

                // A scanned CV has no text; it is marked read so the job does not try it again
                text = extracted.IsSuccess ? extracted.Value.Text : null;
            }

            candidate.IndexCv(fileId, text);
        }

        return candidates.Count;
    }
}
