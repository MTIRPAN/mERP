using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

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

/// <summary>
/// A person who applied (aday, PLAN R3, D49): one per e-mail address in the tenant (user decision). Each application
/// refreshes the contact details and the CV; the pool consent is kept with the version of the text shown.
/// </summary>
[Audited]
public sealed class Candidate : AuditableAggregateRoot<CandidateId>
{
    public const int EmailMaxLength = 254;
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 30;
    public const int CultureMaxLength = 10;
    public const int SourceMaxLength = 50;

    private Candidate()
    {
    }

    /// <summary>Lower case; unique in the tenant.</summary>
    public string Email { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? Phone { get; private set; }

    public string? Culture { get; private set; }

    /// <summary>The candidate source code of the first application (D47 lists).</summary>
    public string Source { get; private set; } = null!;

    /// <summary>The HR employee of an internal candidate.</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>The latest CV (a stored file of the candidate).</summary>
    public Guid? CvFileId { get; private set; }

    /// <summary>Revision of the explicit consent text the candidate accepted to stay in the pool; null without consent.</summary>
    public int? PoolConsentRevision { get; private set; }

    public DateTimeOffset? PoolConsentAt { get; private set; }

    public DateTimeOffset LastAppliedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>The e-mail as stored: trimmed, lower case.</summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

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

    /// <summary>A new application: the contact details are those it gave; an internal one links the employee.</summary>
    public void Applied(CandidateContact contact, Guid? employeeId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(contact);
        FirstName = Text.Required(contact.FirstName, NameMaxLength, nameof(contact.FirstName));
        LastName = Text.Required(contact.LastName, NameMaxLength, nameof(contact.LastName));
        Phone = Text.Optional(contact.Phone, PhoneMaxLength, nameof(contact.Phone)) ?? Phone;
        Culture = Text.Optional(contact.Culture, CultureMaxLength, nameof(contact.Culture)) ?? Culture;
        EmployeeId = employeeId ?? EmployeeId;
        LastAppliedAt = at;
    }

    public void ReplaceCv(Guid fileId) => CvFileId = fileId;

    /// <summary>The candidate agreed to stay in the pool under revision <paramref name="revision"/> of the text.</summary>
    public void ConsentToPool(int revision, DateTimeOffset at)
    {
        PoolConsentRevision = revision;
        PoolConsentAt = at;
    }
}
