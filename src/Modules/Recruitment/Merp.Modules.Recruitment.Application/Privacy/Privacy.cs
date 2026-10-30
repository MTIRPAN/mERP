using System.Globalization;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.Modules.Recruitment.Application.Offers;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Privacy;

/// <summary>What a candidate consented to and when (D55).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CandidateConsentKind>))]
public enum CandidateConsentKind
{
    /// <summary>The disclosure text accepted on an application form.</summary>
    Disclosure,

    /// <summary>The explicit consent to stay in the pool given on an application form.</summary>
    Pool,

    /// <summary>The pool consent renewed with the link of the renewal e-mail.</summary>
    Renewed,

    /// <summary>The pool consent refused with the link.</summary>
    Withdrawn,
}

/// <param name="At">When.</param>
/// <param name="Kind">What.</param>
/// <param name="Revision">The revision of the text accepted; null for a withdrawal.</param>
/// <param name="Culture">The language of the text.</param>
/// <param name="OpeningTitle">The opening of the application form; null for the renewal page.</param>
public sealed record CandidateConsentItem(DateTimeOffset At, CandidateConsentKind Kind, int? Revision, string? Culture, string? OpeningTitle);

/// <summary>The candidate's page of the renewal link (anonymous, D55).</summary>
/// <param name="FirstName">The candidate's first name, for the greeting.</param>
/// <param name="Text">The explicit consent text to accept, in the candidate's language.</param>
/// <param name="Revision">Its revision.</param>
/// <param name="RetentionEndsOn">The day the data is erased without a renewal.</param>
public sealed record ConsentRenewalPage(string FirstName, string Text, int Revision, DateOnly RetentionEndsOn);

/// <summary>What a retention run did (the candidate retention job, D55).</summary>
public sealed record CandidateRetentionResult(int RenewalsRequested, int Anonymized);

/// <summary>
/// The candidate's data as a ZIP (ilgili kişi talebi, D55): the record, the consents, the applications, notes,
/// interviews, evaluations and offers as JSON, with the CVs and offer letters.
/// </summary>
public sealed record ExportCandidateDataQuery(Guid Id) : IQuery<RecruitmentFileDownload>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Privacy;
}

/// <summary>
/// Erases a candidate on request (D55): anonymized at once like at the end of the retention; active applications are
/// withdrawn. A candidate waiting for HR to complete a hire is not erased.
/// </summary>
public sealed record EraseCandidateCommand(uint Version) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Candidates.Privacy;
}

/// <summary>
/// The nightly retention (D55): asks the candidates whose retention ends within 30 days to renew their consent, and
/// anonymizes those whose retention ended and who have no application in the pipeline.
/// </summary>
public sealed record RunCandidateRetentionCommand : ICommand<CandidateRetentionResult>, IBulkCommand;

/// <summary>The candidate's page of the renewal link by its token (anonymous).</summary>
public sealed record GetConsentRenewalPageQuery(string Token) : IQuery<ConsentRenewalPage>;

/// <summary>The candidate renews or refuses the pool consent with the link (anonymous).</summary>
public sealed record RespondToConsentRenewalCommand(bool Accepted) : ICommand
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public string Token { get; init; } = string.Empty;
}

/// <summary>Retention rules of the candidate pool (D55).</summary>
public static class CandidateRetention
{
    /// <summary>The renewal e-mail goes this many days before the retention ends.</summary>
    public const int RenewalNoticeDays = 30;

    public const int BatchSize = 100;

    /// <summary>The day the data is erased: the retention start plus the tenant's retention months.</summary>
    public static DateOnly EndsOn(Candidate candidate, int months) =>
        DateOnly.FromDateTime(candidate.RetentionStart.UtcDateTime).AddMonths(months);

    public static Task<int> MonthsAsync(ISettings settings, CancellationToken cancellationToken) =>
        settings.GetAsync<int>(RecruitmentSettingDefinitions.RetentionMonths, null, cancellationToken);

    /// <summary>The consents of the application forms and the later renewals and withdrawals, newest first.</summary>
    public static List<CandidateConsentItem> Consents(Candidate candidate, IEnumerable<(JobApplication Application, string OpeningTitle)> applications)
    {
        var items = new List<CandidateConsentItem>();
        foreach (var (application, title) in applications)
        {
            if (application.Consent is not { } consent)
            {
                continue;
            }

            items.Add(new CandidateConsentItem(application.AppliedAt, CandidateConsentKind.Disclosure, consent.DisclosureRevision, consent.Culture, title));
            if (consent.PoolConsentRevision is { } pool)
            {
                items.Add(new CandidateConsentItem(application.AppliedAt, CandidateConsentKind.Pool, pool, consent.Culture, title));
            }
        }

        items.AddRange(candidate.ConsentChanges.Select(c => new CandidateConsentItem(
            c.At,
            c.Kind == ConsentChangeKind.Renewed ? CandidateConsentKind.Renewed : CandidateConsentKind.Withdrawn,
            c.Revision,
            c.Culture,
            null)));
        return [.. items.OrderByDescending(i => i.At)];
    }
}

internal sealed class EraseCandidateCommandHandler(IRecruitmentDbContext db, TimeProvider timeProvider) : ICommandHandler<EraseCandidateCommand>
{
    public async Task<Result> Handle(EraseCandidateCommand request, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(candidate).ExpectVersion(request.Version);
        if (await db.JobApplications.AnyAsync(a => a.CandidateId == candidate.Id && a.HireRequestedAt != null, cancellationToken))
        {
            return JobApplicationErrors.HireRequested;
        }

        return candidate.Anonymize(timeProvider.GetUtcNow());
    }
}

/// <summary>
/// The candidate was anonymized (D55): its applications lose the cover letter, the answers and the CV (active ones are
/// withdrawn), the CV analyses are deleted, the offers lose the answer note and the letter, and the files are removed.
/// Runs in the transaction of the anonymization, after the single aggregate check (CLAUDE.md rule 7).
/// </summary>
internal sealed class ClearAnonymizedCandidate(IRecruitmentDbContext db, IFileStore files, ICurrentUser currentUser, TimeProvider timeProvider)
    : IDomainEventHandler<CandidateAnonymized>
{
    /// <summary>Active applications of an erased candidate are withdrawn for "other" reasons (D47 list).</summary>
    public const string WithdrawalReason = "OTHER";

    public async Task Handle(CandidateAnonymized domainEvent, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var applications = await db.JobApplications.Include(a => a.History).Where(a => a.CandidateId == domainEvent.CandidateId).ToListAsync(cancellationToken);
        var openingIds = applications.Select(a => a.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        foreach (var application in applications)
        {
            if (application.Status == ApplicationStatus.Active)
            {
                application.Withdraw(openings[application.OpeningId], WithdrawalReason, null, currentUser.UserId, now);
            }

            application.Anonymize();
        }

        var applicationIds = applications.Select(a => a.Id).ToList();
        db.CvAnalyses.RemoveRange(await db.CvAnalyses.Where(a => applicationIds.Contains(a.Id)).ToListAsync(cancellationToken));
        var letters = new List<Guid>();
        foreach (var offer in await db.JobOffers.Where(o => applicationIds.Contains(o.ApplicationId)).ToListAsync(cancellationToken))
        {
            if (offer.Anonymize() is { } letter)
            {
                letters.Add(letter);
            }
        }

        var candidateId = domainEvent.CandidateId.Value;
        var stored = await db.StoredFiles
            .Where(f => (f.OwnerType == CandidatePrivacyFiles.CandidateOwner && f.OwnerId == candidateId) || letters.Contains(f.Id))
            .ToListAsync(cancellationToken);
        foreach (var file in stored)
        {
            db.StoredFiles.Remove(file);
            files.PurgeAfterCommit(file);
        }
    }
}

internal static class CandidatePrivacyFiles
{
    public const string CandidateOwner = ApplicationIntake.CandidateFileOwner;
}

internal sealed class RunCandidateRetentionCommandHandler(
    IRecruitmentDbContext db,
    ISettings settings,
    ITenantContext tenant,
    ILocalizationSource localization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<RunCandidateRetentionCommand, CandidateRetentionResult>
{
    public async Task<Result<CandidateRetentionResult>> Handle(RunCandidateRetentionCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var months = await CandidateRetention.MonthsAsync(settings, cancellationToken);

        // Every date the retention counts from is before the cut-off, so the retention has ended (or ends within the notice)
        var endedBy = now.AddMonths(-months);
        var noticeBy = endedBy.AddDays(CandidateRetention.RenewalNoticeDays);
        var waiting = db.Candidates
            .Where(c => c.AnonymizedAt == null
                && c.CreatedAt <= noticeBy
                && (c.LastAppliedAt == null || c.LastAppliedAt <= noticeBy)
                && (c.PoolConsentAt == null || c.PoolConsentAt <= noticeBy)
                && !db.JobApplications.Any(a => a.CandidateId == c.Id && a.Status == ApplicationStatus.Active));

        var expired = await waiting
            .Where(c => c.CreatedAt <= endedBy
                && (c.LastAppliedAt == null || c.LastAppliedAt <= endedBy)
                && (c.PoolConsentAt == null || c.PoolConsentAt <= endedBy))
            .Include(c => c.Notes)
            .OrderBy(c => c.CreatedAt)
            .Take(CandidateRetention.BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var candidate in expired)
        {
            candidate.Anonymize(now);
        }

        var expiredIds = expired.Select(c => c.Id).ToList();
        var notice = (await waiting
                .Where(c => !expiredIds.Contains(c.Id))
                .OrderBy(c => c.CreatedAt)
                .Take(CandidateRetention.BatchSize * 2)
                .ToListAsync(cancellationToken))
            .Where(c => c.ConsentRenewalRequestedAt is not { } sent || sent < c.RetentionStart)
            .Take(CandidateRetention.BatchSize)
            .ToList();
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        foreach (var candidate in notice)
        {
            var (token, hash) = OfferRules.NewToken();
            candidate.ConsentRenewalRequested(hash, now);
            var culture = candidate.Culture ?? defaultCulture;
            var endsOn = CandidateRetention.EndsOn(candidate, months);
            await publisher.PublishAsync(
                new ExternalEmailRequested(
                    RecruitmentNotificationTypes.ConsentRenewal,
                    candidate.Email,
                    candidate.FullName,
                    culture,
                    new Dictionary<string, string>
                    {
                        ["candidate"] = candidate.FullName,
                        ["company"] = tenant.RequiredTenant.Name,
                        ["date"] = endsOn.ToString("d", CultureInfo.GetCultureInfo(culture)),
                    })
                {
                    Link = $"/careers/{tenant.RequiredTenant.Code}/consents/{token}",
                },
                cancellationToken);
        }

        return new CandidateRetentionResult(notice.Count, expired.Count);
    }
}

internal sealed class GetConsentRenewalPageQueryHandler(IRecruitmentDbContext db, ISettings settings, ConsentTextResolver texts)
    : IQueryHandler<GetConsentRenewalPageQuery, ConsentRenewalPage>
{
    public async Task<Result<ConsentRenewalPage>> Handle(GetConsentRenewalPageQuery request, CancellationToken cancellationToken)
    {
        var hash = OfferRules.Hash(request.Token);
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.ConsentTokenHash == hash && c.AnonymizedAt == null, cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        var text = await texts.CurrentAsync(ConsentKind.ExplicitConsent, null, candidate.Culture, cancellationToken);
        var months = await CandidateRetention.MonthsAsync(settings, cancellationToken);
        return new ConsentRenewalPage(candidate.FirstName, text.Body, text.Revision, CandidateRetention.EndsOn(candidate, months));
    }
}

internal sealed class RespondToConsentRenewalCommandHandler(IRecruitmentDbContext db, ConsentTextResolver texts, TimeProvider timeProvider)
    : ICommandHandler<RespondToConsentRenewalCommand>
{
    public async Task<Result> Handle(RespondToConsentRenewalCommand request, CancellationToken cancellationToken)
    {
        var hash = OfferRules.Hash(request.Token);
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.ConsentTokenHash == hash && c.AnonymizedAt == null, cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        if (!request.Accepted)
        {
            return candidate.WithdrawPoolConsent(timeProvider.GetUtcNow());
        }

        var text = await texts.CurrentAsync(ConsentKind.ExplicitConsent, null, candidate.Culture, cancellationToken);
        return candidate.RenewPoolConsent(text.Revision, text.Culture, timeProvider.GetUtcNow());
    }
}

internal sealed class ExportCandidateDataQueryHandler(
    IRecruitmentDbContext db,
    IFileStore files,
    ISensitiveReadLog readLog,
    ITextLocalizer localizer,
    ICurrentUser currentUser) : IQueryHandler<ExportCandidateDataQuery, RecruitmentFileDownload>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<RecruitmentFileDownload>> Handle(ExportCandidateDataQuery request, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().Include(c => c.Notes).SingleOrDefaultAsync(c => c.Id == CandidateId.From(request.Id), cancellationToken);
        if (candidate is null)
        {
            return CommonErrors.NotFound;
        }

        var applications = await db.JobApplications.AsNoTracking().Include(a => a.History).Where(a => a.CandidateId == candidate.Id).OrderBy(a => a.AppliedAt).ToListAsync(cancellationToken);
        var openingIds = applications.Select(a => a.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var applicationIds = applications.Select(a => a.Id).ToList();
        var interviews = await db.Interviews.AsNoTracking().Where(i => applicationIds.Contains(i.ApplicationId)).OrderBy(i => i.StartsAt).ToListAsync(cancellationToken);
        var feedback = await db.InterviewFeedbacks.AsNoTracking().Where(f => applicationIds.Contains(f.ApplicationId)).ToListAsync(cancellationToken);
        var offers = await db.JobOffers.AsNoTracking().Where(o => applicationIds.Contains(o.ApplicationId)).ToListAsync(cancellationToken);
        foreach (var offer in offers)
        {
            await readLog.RecordAsync(typeof(JobOffer), offer.Id.Value.ToString(), [nameof(JobOffer.Salary), nameof(JobOffer.Currency), nameof(JobOffer.Period)], cancellationToken);
        }

        var letterIds = offers.Where(o => o.LetterFileId is not null).Select(o => o.LetterFileId!.Value).ToList();
        var stored = await db.StoredFiles.AsNoTracking()
            .Where(f => (f.OwnerType == CandidatePrivacyFiles.CandidateOwner && f.OwnerId == candidate.Id.Value) || letterIds.Contains(f.Id))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        string Title(JobOpeningId id) => openings.TryGetValue(id, out var opening) ? opening.Title : string.Empty;
        string? Stage(JobOpeningId id, Guid? stageId) => openings.TryGetValue(id, out var opening)
            ? opening.Stages.FirstOrDefault(s => s.Id == stageId)?.Name.Get(currentUser.Culture, "tr-TR")
            : null;

        var data = new
        {
            candidate = new
            {
                candidate.Email,
                candidate.FirstName,
                candidate.LastName,
                candidate.Phone,
                candidate.Culture,
                candidate.Source,
                candidate.Location,
                candidate.CurrentTitle,
                candidate.CurrentCompany,
                candidate.ExperienceYears,
                candidate.Education,
                candidate.LinkedInUrl,
                candidate.PortfolioUrl,
                candidate.Tags,
                candidate.CreatedAt,
                candidate.LastAppliedAt,
                candidate.AnonymizedAt,
            },
            consents = CandidateRetention.Consents(candidate, applications.Select(a => (a, Title(a.OpeningId)))),
            applications = applications.Select(a => new
            {
                opening = Title(a.OpeningId),
                a.AppliedAt,
                a.Status,
                stage = Stage(a.OpeningId, a.StageId),
                a.Source,
                a.CoverLetter,
                answers = a.Answers.Select(x => new
                {
                    question = openings.TryGetValue(a.OpeningId, out var opening) ? opening.Form.Questions.FirstOrDefault(q => q.Id == x.QuestionId)?.Text : null,
                    x.Values,
                }),
                history = a.History.OrderBy(h => h.Sequence).Select(h => new { h.At, from = Stage(a.OpeningId, h.FromStageId), to = Stage(a.OpeningId, h.ToStageId), h.Status, h.ReasonCode }),
            }),
            notes = candidate.Notes.OrderBy(n => n.At).Select(n => new { n.At, n.Body }),
            interviews = interviews.Select(i => new { opening = Title(i.OpeningId), i.Type, i.StartsAt, i.EndsAt, i.Status }),
            evaluations = feedback.Select(f => new { f.SubmittedAt, f.Recommendation, f.WeightedScore, f.Scores, f.Answers, f.Summary }),
            offers = offers.Select(o => new { o.Number, o.PositionTitle, o.StartDate, o.Salary, o.Currency, o.Period, o.Benefits, o.ExpiresOn, o.Status, o.RespondedAt }),
            files = stored.Select(f => new { f.FileName, f.ContentType, f.Length, f.CreatedAt }),
        };

        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("aday.json", CompressionLevel.Optimal);
            await using (var json = entry.Open())
            {
                await JsonSerializer.SerializeAsync(json, data, Json, cancellationToken);
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in stored)
            {
                var name = UniqueName(names, $"dosyalar/{Path.GetFileName(file.FileName)}");
                await using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                await using var source = await files.OpenReadAsync(file, cancellationToken);
                await source.CopyToAsync(target, cancellationToken);
            }
        }

        buffer.Position = 0;
        var fileName = localizer.Get("recruitment.candidates.privacy.export_file_name", new Dictionary<string, object?> { ["code"] = candidate.Id.Value.ToString("N")[..8] }, currentUser.Culture);
        return new RecruitmentFileDownload(fileName, "application/zip", buffer.Length, buffer);
    }

    /// <summary>Two files with the same name get a number before the extension.</summary>
    private static string UniqueName(HashSet<string> names, string name)
    {
        var candidate = name;
        for (var i = 2; !names.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
            candidate = $"dosyalar/{candidate}";
        }

        return candidate;
    }
}
