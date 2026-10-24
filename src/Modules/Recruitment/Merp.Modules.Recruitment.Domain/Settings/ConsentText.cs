using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.Recruitment.Domain.Settings;

public readonly record struct ConsentTextId(Guid Value) : IEntityId<ConsentTextId>
{
    public static ConsentTextId From(Guid value) => new(value);
}

/// <summary>The KVKK texts a candidate sees when applying (PLAN R1, D47).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsentKind>))]
public enum ConsentKind
{
    /// <summary>Aydınlatma metni (KVKK md. 10): read and acknowledged.</summary>
    Disclosure,

    /// <summary>Açık rıza metni: given or refused, e.g. to keep the CV in the pool for other openings.</summary>
    ExplicitConsent,
}

/// <summary>
/// One published version of a KVKK text for a company (or every company) and a language (PLAN R1, D47). Versions
/// never change: a candidate's consent names the version they saw, so a change is a new version.
/// </summary>
[Audited]
public sealed class ConsentText : AuditableAggregateRoot<ConsentTextId>
{
    public const int BodyMaxLength = 20_000;
    public const int CultureMaxLength = 10;

    private ConsentText()
    {
    }

    public ConsentKind Kind { get; private set; }

    /// <summary>The company it belongs to; null for every company.</summary>
    public Guid? CompanyId { get; private set; }

    public string Culture { get; private set; } = null!;

    /// <summary>1 for the first version of a kind, company and language, then one more for each.</summary>
    public int Revision { get; private set; }

    /// <summary>Plain text; paragraphs are separated by empty lines.</summary>
    public string Body { get; private set; } = null!;

    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>Publishes the revision after <paramref name="previousRevision"/> (0 when there is none).</summary>
    public static ConsentText Publish(ConsentKind kind, Guid? companyId, string culture, int previousRevision, string body, DateTimeOffset at)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown consent text.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(previousRevision);
        return new ConsentText
        {
            Id = EntityId.New<ConsentTextId>(),
            Kind = kind,
            CompanyId = companyId,
            Culture = Text.Required(culture, CultureMaxLength, nameof(culture)),
            Revision = previousRevision + 1,
            Body = Text.Required(body, BodyMaxLength, nameof(body)),
            PublishedAt = at,
        };
    }
}
