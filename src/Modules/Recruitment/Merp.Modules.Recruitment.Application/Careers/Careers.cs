using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Careers;

/// <summary>A company with openings on the career page.</summary>
public sealed record CareerCompany(Guid Id, string Name, bool HasLogo);

/// <summary>An opening that takes applications, as candidates and employees see it.</summary>
public sealed record CareerOpeningItem(
    Guid Id,
    string Title,
    Guid CompanyId,
    string CompanyName,
    Guid? BranchId,
    string? BranchName,
    WorkModel WorkModel,
    RequisitionEmploymentType EmploymentType,
    OpeningVisibility Visibility,
    DateOnly? PublishFrom,
    DateOnly? PublishUntil);

/// <summary>The career page of the tenant: the open public openings of all its companies (D49).</summary>
public sealed record CareerPage(IReadOnlyList<CareerCompany> Companies, IReadOnlyList<CareerOpeningItem> Openings);

/// <summary>A question of the form; the accepted answers of a knockout question are never shown.</summary>
public sealed record CareerQuestion(Guid Id, string Text, QuestionType Type, bool Required, IReadOnlyList<string> Options);

/// <summary>What the application form asks and the CV it accepts.</summary>
public sealed record CareerForm(bool RequirePhone, bool RequireCv, bool AskCoverLetter, IReadOnlyList<CareerQuestion> Questions, IReadOnlyList<string> CvExtensions, long CvMaxBytes);

/// <summary>A KVKK text shown with the form; the application sends back its revision (D47).</summary>
public sealed record CareerConsent(ConsentKind Kind, int Revision, string Culture, string Body);

/// <summary>An opening with its texts, its form and the KVKK texts to accept.</summary>
public sealed record CareerOpeningDetails(
    CareerOpeningItem Opening,
    string? Description,
    string? Qualifications,
    CareerForm Form,
    CareerConsent Disclosure,
    CareerConsent PoolConsent);

/// <summary>An answer as the form sends it.</summary>
public sealed record ApplicationAnswerInput(Guid QuestionId, IReadOnlyList<string>? Values);

/// <summary>The fields of an application besides the candidate's identity.</summary>
public interface IApplicationFields
{
    string? Phone { get; }

    string? CoverLetter { get; }

    IReadOnlyList<ApplicationAnswerInput>? Answers { get; }

    /// <summary>The disclosure text was read and accepted; required.</summary>
    bool DisclosureAccepted { get; }

    /// <summary>The revision of the disclosure text shown.</summary>
    int DisclosureRevision { get; }

    /// <summary>The candidate agrees to stay in the pool for other openings; optional.</summary>
    bool PoolConsent { get; }

    /// <summary>The revision of the explicit consent text shown, when agreed.</summary>
    int? PoolConsentRevision { get; }

    /// <summary>The language of the page; the e-mails follow it.</summary>
    string? Culture { get; }

    FileUpload? Cv { get; }
}

/// <summary>The career page (anonymous, D20).</summary>
public sealed record GetCareerPageQuery : IQuery<CareerPage>;

/// <summary>An open public opening (anonymous, D20); the KVKK texts in <see cref="Culture"/>.</summary>
public sealed record GetCareerOpeningQuery(Guid Id, string? Culture = null) : IQuery<CareerOpeningDetails>;

/// <summary>The logo of a company with an open public opening (anonymous, D20).</summary>
public sealed record GetCareerLogoQuery(Guid CompanyId) : IQuery<CompanyLogo>;

/// <summary>
/// An application from the career page (anonymous, D20, D49). <see cref="Website"/> is the honeypot: people leave it
/// empty, a bot fills it and its application is dropped silently.
/// </summary>
public sealed record SubmitApplicationCommand(
    Guid OpeningId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? CoverLetter,
    IReadOnlyList<ApplicationAnswerInput>? Answers,
    bool DisclosureAccepted,
    int DisclosureRevision,
    bool PoolConsent,
    int? PoolConsentRevision,
    string? Culture,
    string? Website) : ICommand, IApplicationFields
{
    [JsonIgnore]
    public FileUpload? Cv { get; init; }
}

/// <summary>The openings an employee may apply to: internal and public ones that take applications (self service).</summary>
public sealed record GetMyOpeningsQuery : IQuery<IReadOnlyList<CareerOpeningItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Self.Apply;
}

/// <summary>An opening an employee may apply to, with its form.</summary>
public sealed record GetMyOpeningQuery(Guid Id, string? Culture = null) : IQuery<CareerOpeningDetails>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Self.Apply;
}

/// <summary>An employee applies from self service; name and e-mail come from the personnel record and the user (D49).</summary>
public sealed record SubmitMyApplicationCommand(
    string? Phone,
    string? CoverLetter,
    IReadOnlyList<ApplicationAnswerInput>? Answers,
    bool DisclosureAccepted,
    int DisclosureRevision,
    bool PoolConsent,
    int? PoolConsentRevision,
    string? Culture) : ICommand, IApplicationFields, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid OpeningId { get; init; }

    [JsonIgnore]
    public FileUpload? Cv { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Self.Apply;
}

/// <summary>An application of the employee.</summary>
public sealed record MyApplicationItem(Guid Id, Guid OpeningId, string OpeningTitle, string? CompanyName, ApplicationStatus Status, DateTimeOffset AppliedAt);

/// <summary>The employee's own applications, newest first.</summary>
public sealed record GetMyApplicationsQuery : IQuery<IReadOnlyList<MyApplicationItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Self.Apply;
}

public static class CareerErrors
{
    public const string ConsentRequiredCode = "recruitment.applications.errors.consent_required";
    public const string ConsentChangedCode = "recruitment.applications.errors.consent_changed";
    public const string AlreadyAppliedCode = "recruitment.applications.errors.already_applied";
    public const string PhoneRequiredCode = "recruitment.applications.errors.phone_required";

    public static readonly Error NotEmployee = Error.Validation([new FieldError("opening", "recruitment.applications.errors.not_employee")]);
}

/// <summary>Who applies: the e-mail and contact of the candidate, and the employee of an internal one.</summary>
public sealed record Applicant(string Email, CandidateContact Contact, Guid? EmployeeId);

/// <summary>Builds what candidates and employees see of openings (D49).</summary>
public sealed class CareerCatalog(IRecruitmentDbContext db, IOrganizationLookup organization, ConsentTextResolver consents, TimeProvider timeProvider)
{
    /// <summary>CVs: PDF or DOCX (a legacy .doc may carry macros and cannot be checked), at most 5 MB.</summary>
    public static readonly FileRules CvRules = new(new HashSet<string>(["pdf", "docx"], StringComparer.Ordinal), 5 * 1024 * 1024);

    /// <summary>The openings that take applications today in their company's time zone; public ones only unless <paramref name="internalToo"/>.</summary>
    public async Task<List<JobOpening>> OpenAsync(bool internalToo, CancellationToken cancellationToken)
    {
        var published = await db.JobOpenings.AsNoTracking()
            .Where(o => o.Status == OpeningStatus.Published && (internalToo || o.Visibility == OpeningVisibility.Public))
            .ToListAsync(cancellationToken);
        var zones = (await organization.GetCompaniesAsync([.. published.Select(o => o.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id);
        return
        [
            .. published
                .Where(o => zones.TryGetValue(o.CompanyId, out var company) && company.IsActive
                    && o.IsOpenOn(RecruitmentClock.Today(timeProvider, company.TimeZoneId)))
                .OrderByDescending(o => o.PublishFrom)
                .ThenBy(o => o.Title),
        ];
    }

    public async Task<JobOpening?> FindOpenAsync(Guid id, bool internalToo, CancellationToken cancellationToken) =>
        (await OpenAsync(internalToo, cancellationToken)).FirstOrDefault(o => o.Id == JobOpeningId.From(id));

    public async Task<List<CareerOpeningItem>> ItemsAsync(IReadOnlyList<JobOpening> openings, CancellationToken cancellationToken)
    {
        var companies = (await organization.GetCompaniesAsync([.. openings.Select(o => o.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        var branches = (await organization.GetBranchesAsync([.. openings.Where(o => o.BranchId is not null).Select(o => o.BranchId!.Value).Distinct()], cancellationToken))
            .ToDictionary(b => b.Id, b => b.Name);
        return
        [
            .. openings.Select(o => new CareerOpeningItem(
                o.Id.Value,
                o.Title,
                o.CompanyId,
                companies.GetValueOrDefault(o.CompanyId) ?? string.Empty,
                o.BranchId,
                o.BranchId is { } branch ? branches.GetValueOrDefault(branch) : null,
                o.WorkModel,
                o.EmploymentType,
                o.Visibility,
                o.PublishFrom,
                o.PublishUntil)),
        ];
    }

    public async Task<CareerOpeningDetails> DetailsAsync(JobOpening opening, string? culture, CancellationToken cancellationToken)
    {
        var disclosure = await consents.CurrentAsync(ConsentKind.Disclosure, opening.CompanyId, culture, cancellationToken);
        var pool = await consents.CurrentAsync(ConsentKind.ExplicitConsent, opening.CompanyId, culture, cancellationToken);
        var form = opening.Form;
        return new CareerOpeningDetails(
            (await ItemsAsync([opening], cancellationToken)).Single(),
            opening.Description,
            opening.Qualifications,
            new CareerForm(
                form.RequirePhone,
                form.RequireCv,
                form.AskCoverLetter,
                [.. form.Questions.Select(q => new CareerQuestion(q.Id, q.Text, q.Type, q.Required, q.Options))],
                [.. CvRules.Extensions.Order(StringComparer.Ordinal)],
                CvRules.MaxBytes),
            new CareerConsent(disclosure.Kind, disclosure.Revision, disclosure.Culture, disclosure.Body),
            new CareerConsent(pool.Kind, pool.Revision, pool.Culture, pool.Body));
    }
}

/// <summary>Receives applications from the career page and from self service (D49).</summary>
public sealed class ApplicationIntake(
    IRecruitmentDbContext db,
    IFileStore files,
    ConsentTextResolver consents,
    IOrganizationLookup organization,
    ILocalizationSource localization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public const string CandidateFileOwner = "recruitment.candidate";
    public const string CareerPageSource = "CAREER_PAGE";
    public const string InternalSource = "INTERNAL";

    public async Task<Result> ReceiveAsync(JobOpening opening, Applicant applicant, IApplicationFields fields, CancellationToken cancellationToken)
    {
        var company = (await organization.GetCompaniesAsync([opening.CompanyId], cancellationToken)).Single();
        var culture = string.IsNullOrWhiteSpace(fields.Culture) ? (await localization.GetAsync(cancellationToken)).DefaultCulture : fields.Culture;
        var consent = await ConsentAsync(opening.CompanyId, culture, fields, cancellationToken);
        if (consent.IsFailure)
        {
            return consent.Error;
        }

        if (opening.Form.RequirePhone && string.IsNullOrWhiteSpace(applicant.Contact.Phone))
        {
            return Error.Validation([new FieldError("phone", CareerErrors.PhoneRequiredCode)]);
        }

        var email = Candidate.NormalizeEmail(applicant.Email);
        var existing = await db.Candidates.AsNoTracking().Where(c => c.Email == email).Select(c => (CandidateId?)c.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is { } known && await db.JobApplications.AnyAsync(a => a.CandidateId == known && a.OpeningId == opening.Id, cancellationToken))
        {
            return Error.Validation([new FieldError("email", CareerErrors.AlreadyAppliedCode)]);
        }

        var candidateId = existing ?? new CandidateId(Guid.CreateVersion7());
        Guid? cvFileId = null;
        if (fields.Cv is { } upload)
        {
            var stored = await files.SaveAsync(CandidateFileOwner, candidateId.Value, StoredFilePurposes.Attachment, upload, CareerCatalog.CvRules, cancellationToken);
            if (stored.IsFailure)
            {
                return RenameFileField(stored.Error);
            }

            db.StoredFiles.Add(stored.Value);
            cvFileId = stored.Value.Id;
        }

        var internalApplication = applicant.EmployeeId is not null;
        var terms = new ApplicationTerms(
            candidateId,
            fields.CoverLetter,
            [.. (fields.Answers ?? []).Select(a => new ApplicationAnswer(a.QuestionId, a.Values ?? []))],
            cvFileId,
            consent.Value,
            internalApplication,
            internalApplication ? InternalSource : CareerPageSource);
        var today = RecruitmentClock.Today(timeProvider, company.TimeZoneId);
        var application = JobApplication.Submit(opening, terms, email, applicant.Contact with { Culture = culture }, applicant.EmployeeId, today, timeProvider.GetUtcNow());
        if (application.IsFailure)
        {
            return application.Error;
        }

        db.JobApplications.Add(application.Value);
        await publisher.PublishAsync(
            new ExternalEmailRequested(
                RecruitmentNotificationTypes.ApplicationReceived,
                email,
                $"{applicant.Contact.FirstName} {applicant.Contact.LastName}".Trim(),
                culture,
                new Dictionary<string, string>
                {
                    ["candidate"] = $"{applicant.Contact.FirstName} {applicant.Contact.LastName}".Trim(),
                    ["position"] = opening.Title,
                    ["company"] = company.ShortName,
                })
            {
                CompanyId = opening.CompanyId,
            },
            cancellationToken);
        return Result.Success();
    }

    /// <summary>The disclosure must be accepted and both texts must be the versions the candidate was shown.</summary>
    private async Task<Result<ApplicationConsent>> ConsentAsync(Guid companyId, string culture, IApplicationFields fields, CancellationToken cancellationToken)
    {
        if (!fields.DisclosureAccepted)
        {
            return Error.Validation([new FieldError("disclosureAccepted", CareerErrors.ConsentRequiredCode)]);
        }

        var disclosure = await consents.CurrentAsync(ConsentKind.Disclosure, companyId, culture, cancellationToken);
        if (disclosure.Revision != fields.DisclosureRevision)
        {
            return Error.Validation([new FieldError("disclosureRevision", CareerErrors.ConsentChangedCode)]);
        }

        int? poolRevision = null;
        if (fields.PoolConsent)
        {
            var pool = await consents.CurrentAsync(ConsentKind.ExplicitConsent, companyId, culture, cancellationToken);
            if (pool.Revision != fields.PoolConsentRevision)
            {
                return Error.Validation([new FieldError("poolConsentRevision", CareerErrors.ConsentChangedCode)]);
            }

            poolRevision = pool.Revision;
        }

        return new ApplicationConsent(disclosure.Revision, disclosure.Culture, poolRevision);
    }

    /// <summary>File errors are on the <c>file</c> field; the form names it <c>cv</c>.</summary>
    private static Error RenameFileField(Error error) =>
        error.Fields.Count > 0 ? Error.Validation([.. error.Fields.Select(e => e with { Field = "cv" })]) : error;
}

internal static class ApplicationFieldRules
{
    public static void Add<T>(AbstractValidator<T> validator)
        where T : IApplicationFields
    {
        validator.RuleFor(c => c.Phone).MaximumLength(Candidate.PhoneMaxLength);
        validator.RuleFor(c => c.CoverLetter).MaximumLength(JobApplication.CoverLetterMaxLength);
        validator.RuleFor(c => c.Culture).MaximumLength(Candidate.CultureMaxLength);
        validator.RuleFor(c => c.Answers).Must(a => a is null || a.Count <= ApplicationForm.MaxQuestions * 2).WithErrorCode(CommonErrors.ValidationCode);
    }
}

internal sealed class SubmitApplicationCommandValidator : AbstractValidator<SubmitApplicationCommand>
{
    public SubmitApplicationCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(Candidate.NameMaxLength);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(Candidate.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(Candidate.EmailMaxLength).EmailAddress();
        ApplicationFieldRules.Add(this);
    }
}

internal sealed class SubmitMyApplicationCommandValidator : AbstractValidator<SubmitMyApplicationCommand>
{
    public SubmitMyApplicationCommandValidator() => ApplicationFieldRules.Add(this);
}

internal sealed class GetCareerPageQueryHandler(CareerCatalog catalog, ICompanyLogos logos) : IQueryHandler<GetCareerPageQuery, CareerPage>
{
    public async Task<Result<CareerPage>> Handle(GetCareerPageQuery request, CancellationToken cancellationToken)
    {
        var items = await catalog.ItemsAsync(await catalog.OpenAsync(internalToo: false, cancellationToken), cancellationToken);
        var withLogo = await logos.WithLogoAsync([.. items.Select(i => i.CompanyId).Distinct()], cancellationToken);
        var companies = items
            .GroupBy(i => i.CompanyId)
            .Select(g => new CareerCompany(g.Key, g.First().CompanyName, withLogo.Contains(g.Key)))
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        return new CareerPage(companies, items);
    }
}

internal sealed class GetCareerOpeningQueryHandler(CareerCatalog catalog) : IQueryHandler<GetCareerOpeningQuery, CareerOpeningDetails>
{
    public async Task<Result<CareerOpeningDetails>> Handle(GetCareerOpeningQuery request, CancellationToken cancellationToken) =>
        await catalog.FindOpenAsync(request.Id, internalToo: false, cancellationToken) is { } opening
            ? await catalog.DetailsAsync(opening, request.Culture, cancellationToken)
            : CommonErrors.NotFound;
}

internal sealed class GetCareerLogoQueryHandler(CareerCatalog catalog, ICompanyLogos logos) : IQueryHandler<GetCareerLogoQuery, CompanyLogo>
{
    public async Task<Result<CompanyLogo>> Handle(GetCareerLogoQuery request, CancellationToken cancellationToken)
    {
        // Only companies on the career page: the page must not list the tenant's companies
        if (!(await catalog.OpenAsync(internalToo: false, cancellationToken)).Any(o => o.CompanyId == request.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        return await logos.OpenAsync(request.CompanyId, cancellationToken) is { } logo ? logo : CommonErrors.NotFound;
    }
}

internal sealed class SubmitApplicationCommandHandler(CareerCatalog catalog, ApplicationIntake intake) : ICommandHandler<SubmitApplicationCommand>
{
    public async Task<Result> Handle(SubmitApplicationCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Website))
        {
            return Result.Success();
        }

        if (await catalog.FindOpenAsync(request.OpeningId, internalToo: false, cancellationToken) is not { } opening)
        {
            return CommonErrors.NotFound;
        }

        var applicant = new Applicant(request.Email!, new CandidateContact(request.FirstName!, request.LastName!, request.Phone, null), null);
        return await intake.ReceiveAsync(opening, applicant, request, cancellationToken);
    }
}

/// <summary>The employee behind the current user, as an applicant: name from the personnel record, e-mail of the user.</summary>
public sealed class SelfApplicant(ICurrentUser currentUser, IUserDirectory users, IEmployeeLookup employees)
{
    public async Task<Result<Applicant>> FindAsync(string? phone, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || (await users.GetAsync([userId], cancellationToken)).SingleOrDefault() is not { EmployeeId: { } employeeId } user
            || await employees.GetContactAsync(employeeId, cancellationToken) is not { } contact)
        {
            return CareerErrors.NotEmployee;
        }

        var email = user.Email ?? contact.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return CareerErrors.NotEmployee;
        }

        var number = string.IsNullOrWhiteSpace(phone) ? contact.Phone : phone;
        return new Applicant(email, new CandidateContact(contact.FirstName, contact.LastName, number, null), employeeId);
    }
}

internal sealed class GetMyOpeningsQueryHandler(CareerCatalog catalog) : IQueryHandler<GetMyOpeningsQuery, IReadOnlyList<CareerOpeningItem>>
{
    public async Task<Result<IReadOnlyList<CareerOpeningItem>>> Handle(GetMyOpeningsQuery request, CancellationToken cancellationToken) =>
        await catalog.ItemsAsync(await catalog.OpenAsync(internalToo: true, cancellationToken), cancellationToken);
}

internal sealed class GetMyOpeningQueryHandler(CareerCatalog catalog) : IQueryHandler<GetMyOpeningQuery, CareerOpeningDetails>
{
    public async Task<Result<CareerOpeningDetails>> Handle(GetMyOpeningQuery request, CancellationToken cancellationToken) =>
        await catalog.FindOpenAsync(request.Id, internalToo: true, cancellationToken) is { } opening
            ? await catalog.DetailsAsync(opening, request.Culture, cancellationToken)
            : CommonErrors.NotFound;
}

internal sealed class SubmitMyApplicationCommandHandler(CareerCatalog catalog, ApplicationIntake intake, SelfApplicant self)
    : ICommandHandler<SubmitMyApplicationCommand>
{
    public async Task<Result> Handle(SubmitMyApplicationCommand request, CancellationToken cancellationToken)
    {
        if (await catalog.FindOpenAsync(request.OpeningId, internalToo: true, cancellationToken) is not { } opening)
        {
            return CommonErrors.NotFound;
        }

        var applicant = await self.FindAsync(request.Phone, cancellationToken);
        return applicant.IsFailure ? applicant.Error : await intake.ReceiveAsync(opening, applicant.Value, request, cancellationToken);
    }
}

internal sealed class GetMyApplicationsQueryHandler(IRecruitmentDbContext db, ICurrentUser currentUser, IUserDirectory users, IOrganizationLookup organization)
    : IQueryHandler<GetMyApplicationsQuery, IReadOnlyList<MyApplicationItem>>
{
    public async Task<Result<IReadOnlyList<MyApplicationItem>>> Handle(GetMyApplicationsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || (await users.GetAsync([userId], cancellationToken)).SingleOrDefault() is not { EmployeeId: { } employeeId })
        {
            return Array.Empty<MyApplicationItem>();
        }

        var rows = await db.JobApplications.AsNoTracking()
            .Join(db.Candidates.AsNoTracking(), a => a.CandidateId, c => c.Id, (a, c) => new { Application = a, c.EmployeeId })
            .Where(x => x.EmployeeId == employeeId)
            .Join(db.JobOpenings.AsNoTracking(), x => x.Application.OpeningId, o => o.Id, (x, o) => new
            {
                Id = x.Application.Id.Value,
                OpeningId = o.Id.Value,
                o.Title,
                o.CompanyId,
                x.Application.Status,
                x.Application.AppliedAt,
            })
            .OrderByDescending(x => x.AppliedAt)
            .ToListAsync(cancellationToken);
        var companies = (await organization.GetCompaniesAsync([.. rows.Select(r => r.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        return rows.Select(r => new MyApplicationItem(r.Id, r.OpeningId, r.Title, companies.GetValueOrDefault(r.CompanyId), r.Status, r.AppliedAt)).ToList();
    }
}

/// <summary>Registers the candidate of a new application, or refreshes their contact details and CV (rule 7).</summary>
internal sealed class RegisterApplicationCandidate(IRecruitmentDbContext db) : IDomainEventHandler<ApplicationSubmitted>
{
    public async Task Handle(ApplicationSubmitted domainEvent, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == domainEvent.CandidateId, cancellationToken);
        if (candidate is null)
        {
            candidate = Candidate.Register(domainEvent.CandidateId, domainEvent.Email, domainEvent.Contact, domainEvent.Source, domainEvent.EmployeeId, domainEvent.At);
            db.Candidates.Add(candidate);
        }
        else
        {
            candidate.Applied(domainEvent.Contact, domainEvent.EmployeeId, domainEvent.At);
        }

        if (domainEvent.CvFileId is { } cv)
        {
            candidate.ReplaceCv(cv);
        }

        if (domainEvent.PoolConsentRevision is { } revision)
        {
            candidate.ConsentToPool(revision, domainEvent.At);
        }
    }
}
