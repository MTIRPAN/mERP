using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Candidates;

public readonly record struct CandidateId(Guid Value) : IEntityId<CandidateId>
{
    public static CandidateId From(Guid value) => new(value);
}

/// <summary>How to reach a candidate, as the latest application gave it.</summary>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="Phone">Phone number; null when not given.</param>
/// <param name="Culture">The language the candidate uses, for the e-mails.</param>
public sealed record CandidateContact(string FirstName, string LastName, string? Phone, string? Culture);

/// <summary>What HR knows of a candidate's background (PLAN R4, D50); every field is optional.</summary>
/// <param name="Location">Where the candidate lives, e.g. a city.</param>
/// <param name="CurrentTitle">The candidate's present job title.</param>
/// <param name="CurrentCompany">The candidate's present employer.</param>
/// <param name="ExperienceYears">Years of work experience.</param>
/// <param name="Education">School, degree and field in free text.</param>
/// <param name="LinkedInUrl">Profile link (http or https).</param>
/// <param name="PortfolioUrl">Portfolio or personal site (http or https).</param>
public sealed record CandidateProfile(
    string? Location,
    string? CurrentTitle,
    string? CurrentCompany,
    decimal? ExperienceYears,
    string? Education,
    string? LinkedInUrl,
    string? PortfolioUrl)
{
    public const int TextMaxLength = 150;
    public const int EducationMaxLength = 300;
    public const int UrlMaxLength = 300;
    public const decimal MaxExperienceYears = 60;

    public static CandidateProfile Empty { get; } = new(null, null, null, null, null, null, null);
}

/// <summary>
/// A person in the candidate pool (aday, PLAN R3–R4, D49, D50): one per e-mail address in the tenant (user decision).
/// Applications refresh the contact details and the CV; HR keeps the profile, the tags and notes. The full text search
/// covers the names, the profile, the tags, the notes and the text of the latest CV (D50).
/// </summary>
[Audited]
public sealed class Candidate : AuditableAggregateRoot<CandidateId>
{
    public const int EmailMaxLength = 254;
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 30;
    public const int CultureMaxLength = 10;
    public const int SourceMaxLength = 50;
    public const int MaxTags = 20;
    public const int TagTextMaxLength = 2000;
    public const int CvTextMaxLength = 100_000;

    private readonly List<CandidateNote> _notes = [];

    private Candidate()
    {
    }

    /// <summary>Lower case; unique in the tenant.</summary>
    public string Email { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }

    /// <summary>The last ten digits of the phone, to find the same person under another e-mail (D50).</summary>
    public string? NormalizedPhone { get; private set; }

    public string? Culture { get; private set; }

    /// <summary>The candidate source code (D47 lists): of the first application, or chosen by HR.</summary>
    public string Source { get; private set; } = null!;

    /// <summary>The employee who referred the candidate, for the referral source.</summary>
    public Guid? ReferrerEmployeeId { get; private set; }

    /// <summary>The HR employee of an internal candidate.</summary>
    public Guid? EmployeeId { get; private set; }

    public string? Location { get; private set; }

    public string? CurrentTitle { get; private set; }

    public string? CurrentCompany { get; private set; }

    public decimal? ExperienceYears { get; private set; }

    public string? Education { get; private set; }

    public string? LinkedInUrl { get; private set; }

    public string? PortfolioUrl { get; private set; }

    /// <summary>Tag codes (D47 lists).</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>The names of the tags, for the search.</summary>
    public string? TagText { get; private set; }

    /// <summary>The bodies of the notes, for the search; kept by the note methods.</summary>
    public string? NotesText { get; private set; }

    /// <summary>The latest CV (a stored file of the candidate).</summary>
    public Guid? CvFileId { get; private set; }

    /// <summary>The text of <see cref="CvTextFileId"/>, for the search; read in the background (D50).</summary>
    public string? CvText { get; private set; }

    /// <summary>The CV whose text <see cref="CvText"/> holds; behind <see cref="CvFileId"/> until it is read.</summary>
    public Guid? CvTextFileId { get; private set; }

    /// <summary>Revision of the explicit consent text the candidate accepted to stay in the pool; null without consent.</summary>
    public int? PoolConsentRevision { get; private set; }

    public DateTimeOffset? PoolConsentAt { get; private set; }

    /// <summary>The latest application; null for a candidate HR added who has not applied.</summary>
    public DateTimeOffset? LastAppliedAt { get; private set; }

    public IReadOnlyCollection<CandidateNote> Notes => _notes.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>The e-mail as stored: trimmed, lower case.</summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>The last ten digits of a phone number, so 0532…, +90 532… and 532… match; null without digits.</summary>
    public static string? NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>The candidate of a first application.</summary>
    public static Candidate Register(CandidateId id, string email, CandidateContact contact, string source, Guid? employeeId, DateTimeOffset at)
    {
        var candidate = new Candidate
        {
            Id = id,
            Email = Text.Required(NormalizeEmail(email), EmailMaxLength, nameof(email)),
            Source = Text.Required(source, SourceMaxLength, nameof(source)),
        };
        candidate.Applied(contact, employeeId, at);
        return candidate;
    }

    /// <summary>A candidate HR adds to the pool (D50), e.g. from a referral or an agency.</summary>
    public static Candidate Add(string email, CandidateContact contact, CandidateProfile profile, string source, Guid? referrerEmployeeId)
    {
        var candidate = new Candidate { Id = EntityId.New<CandidateId>() };
        candidate.Update(email, contact, profile, source, referrerEmployeeId);
        return candidate;
    }

    /// <summary>A new application: the contact details are those it gave; an internal one links the employee.</summary>
    public void Applied(CandidateContact contact, Guid? employeeId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(contact);
        FirstName = Text.Required(contact.FirstName, NameMaxLength, nameof(contact.FirstName));
        LastName = Text.Required(contact.LastName, NameMaxLength, nameof(contact.LastName));
        SetPhone(Text.Optional(contact.Phone, PhoneMaxLength, nameof(contact.Phone)) ?? Phone);
        Culture = Text.Optional(contact.Culture, CultureMaxLength, nameof(contact.Culture)) ?? Culture;
        EmployeeId = employeeId ?? EmployeeId;
        LastAppliedAt = at;
    }

    /// <summary>HR corrects the contact details and the profile. The e-mail stays unique; the caller checks it.</summary>
    public void Update(string email, CandidateContact contact, CandidateProfile profile, string source, Guid? referrerEmployeeId)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(profile);
        Email = Text.Required(NormalizeEmail(email), EmailMaxLength, nameof(email));
        FirstName = Text.Required(contact.FirstName, NameMaxLength, nameof(contact.FirstName));
        LastName = Text.Required(contact.LastName, NameMaxLength, nameof(contact.LastName));
        SetPhone(Text.Optional(contact.Phone, PhoneMaxLength, nameof(contact.Phone)));
        Culture = Text.Optional(contact.Culture, CultureMaxLength, nameof(contact.Culture)) ?? Culture;
        Source = Text.Required(source, SourceMaxLength, nameof(source));
        ReferrerEmployeeId = referrerEmployeeId;
        Location = Text.Optional(profile.Location, CandidateProfile.TextMaxLength, nameof(profile.Location));
        CurrentTitle = Text.Optional(profile.CurrentTitle, CandidateProfile.TextMaxLength, nameof(profile.CurrentTitle));
        CurrentCompany = Text.Optional(profile.CurrentCompany, CandidateProfile.TextMaxLength, nameof(profile.CurrentCompany));
        ExperienceYears = profile.ExperienceYears is < 0 or > CandidateProfile.MaxExperienceYears
            ? throw new ArgumentOutOfRangeException(nameof(profile), profile.ExperienceYears, "Experience is 0 to 60 years.")
            : profile.ExperienceYears;
        Education = Text.Optional(profile.Education, CandidateProfile.EducationMaxLength, nameof(profile.Education));
        LinkedInUrl = Text.Optional(profile.LinkedInUrl, CandidateProfile.UrlMaxLength, nameof(profile.LinkedInUrl));
        PortfolioUrl = Text.Optional(profile.PortfolioUrl, CandidateProfile.UrlMaxLength, nameof(profile.PortfolioUrl));
    }

    /// <summary>The tags (codes) and their names for the search.</summary>
    public void SetTags(IReadOnlyList<string> codes, string? names)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var distinct = codes.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > MaxTags)
        {
            throw new ArgumentOutOfRangeException(nameof(codes), distinct.Count, "At most 20 tags.");
        }

        Tags = distinct;
        TagText = Truncate(names, TagTextMaxLength);
    }

    /// <summary>A newer CV; its text is read later (<see cref="IndexCv"/>).</summary>
    public void ReplaceCv(Guid fileId) => CvFileId = fileId;

    /// <summary>The text of a CV of the candidate was read; an older CV's text does not replace the latest one's.</summary>
    public void IndexCv(Guid fileId, string? text)
    {
        if (fileId != CvFileId)
        {
            return;
        }

        CvTextFileId = fileId;
        CvText = Truncate(text, CvTextMaxLength);
    }

    /// <summary>The candidate agreed to stay in the pool under revision <paramref name="revision"/> of the text.</summary>
    public void ConsentToPool(int revision, DateTimeOffset at)
    {
        PoolConsentRevision = revision;
        PoolConsentAt = at;
    }

    /// <summary>A note of the team; <paramref name="mentionedUserIds"/> are told about it. The notes must be loaded.</summary>
    public CandidateNote AddNote(string body, IReadOnlyList<Guid> mentionedUserIds, Guid? authorUserId, DateTimeOffset at)
    {
        var note = new CandidateNote(Id, Text.Required(body, CandidateNote.BodyMaxLength, nameof(body)), [.. mentionedUserIds.Distinct()], authorUserId, at);
        _notes.Add(note);
        RefreshNotesText();
        return note;
    }

    /// <summary>Removes a note; only its author does. The notes must be loaded.</summary>
    public Result RemoveNote(Guid noteId, Guid? userId)
    {
        var note = _notes.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
        {
            return CommonErrors.NotFound;
        }

        if (note.AuthorUserId is null || note.AuthorUserId != userId)
        {
            return CandidateErrors.NotNoteAuthor;
        }

        _notes.Remove(note);
        RefreshNotesText();
        return Result.Success();
    }

    /// <summary>
    /// Takes in a duplicate (D50): empty details are filled from it, the tags and notes join, the newer consent and
    /// application win. Both candidates' notes must be loaded. <see cref="CandidateMerged"/> moves its applications
    /// and files and removes it.
    /// </summary>
    public Result Absorb(Candidate duplicate, string? tagNames)
    {
        ArgumentNullException.ThrowIfNull(duplicate);
        if (duplicate.Id == Id)
        {
            return CandidateErrors.MergeSelf;
        }

        SetPhone(Phone ?? duplicate.Phone);
        Culture ??= duplicate.Culture;
        EmployeeId ??= duplicate.EmployeeId;
        ReferrerEmployeeId ??= duplicate.ReferrerEmployeeId;
        Location ??= duplicate.Location;
        CurrentTitle ??= duplicate.CurrentTitle;
        CurrentCompany ??= duplicate.CurrentCompany;
        ExperienceYears ??= duplicate.ExperienceYears;
        Education ??= duplicate.Education;
        LinkedInUrl ??= duplicate.LinkedInUrl;
        PortfolioUrl ??= duplicate.PortfolioUrl;
        SetTags([.. Tags.Concat(duplicate.Tags).Distinct(StringComparer.Ordinal).Take(MaxTags)], tagNames);
        if (CvFileId is null && duplicate.CvFileId is not null)
        {
            CvFileId = duplicate.CvFileId;
            CvTextFileId = duplicate.CvTextFileId;
            CvText = duplicate.CvText;
        }

        if (duplicate.PoolConsentAt is { } consentAt && (PoolConsentAt is null || consentAt > PoolConsentAt))
        {
            ConsentToPool(duplicate.PoolConsentRevision!.Value, consentAt);
        }

        if (duplicate.LastAppliedAt is { } appliedAt && (LastAppliedAt is null || appliedAt > LastAppliedAt))
        {
            LastAppliedAt = appliedAt;
        }

        foreach (var note in duplicate.Notes)
        {
            _notes.Add(new CandidateNote(Id, note.Body, note.MentionedUserIds, note.AuthorUserId, note.At));
        }

        RefreshNotesText();
        Raise(new CandidateMerged(Id, duplicate.Id));
        return Result.Success();
    }

    private void SetPhone(string? phone)
    {
        Phone = phone;
        NormalizedPhone = NormalizePhone(phone);
    }

    private void RefreshNotesText() =>
        NotesText = _notes.Count == 0 ? null : string.Join('\n', _notes.OrderBy(n => n.At).Select(n => n.Body));

    private static string? Truncate(string? text, int maxLength) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length > maxLength ? text[..maxLength] : text;
}

/// <summary>A note of the team on a candidate (D50); the users mentioned in it are notified.</summary>
public sealed class CandidateNote
{
    public const int BodyMaxLength = 2000;

    private CandidateNote()
    {
    }

    internal CandidateNote(CandidateId candidateId, string body, IReadOnlyList<Guid> mentionedUserIds, Guid? authorUserId, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        CandidateId = candidateId;
        Body = body;
        MentionedUserIds = mentionedUserIds;
        AuthorUserId = authorUserId;
        At = at;
    }

    public Guid Id { get; private set; }

    public CandidateId CandidateId { get; private set; }

    public string Body { get; private set; } = null!;

    public IReadOnlyList<Guid> MentionedUserIds { get; private set; } = [];

    public Guid? AuthorUserId { get; private set; }

    public DateTimeOffset At { get; private set; }
}

/// <summary>A duplicate was taken into <paramref name="TargetId"/>: its applications and files move, then it is removed.</summary>
public sealed record CandidateMerged(CandidateId TargetId, CandidateId DuplicateId) : IDomainEvent;

public static class CandidateErrors
{
    public static readonly Error NotNoteAuthor = Error.Forbidden("recruitment.candidates.errors.not_note_author");
    public static readonly Error MergeSelf = Error.Validation([new FieldError("duplicateId", "recruitment.candidates.errors.merge_self")]);
}
