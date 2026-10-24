using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Settings;

/// <summary>
/// The published versions of the KVKK texts (D47), newest first: of every company and of the companies the caller
/// reads; narrowed to a kind and a company when given.
/// </summary>
public sealed record GetConsentTextsQuery(ConsentKind? Kind = null, Guid? CompanyId = null) : IQuery<IReadOnlyList<ConsentTextItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Settings.Read;
}

/// <remarks>Revision 0 is the built-in text, used while the tenant has published none.</remarks>
public sealed record ConsentTextItem(Guid? Id, ConsentKind Kind, Guid? CompanyId, string Culture, int Revision, string Body, DateTimeOffset? PublishedAt);

/// <summary>
/// The text a candidate of <see cref="CompanyId"/> sees in <see cref="Culture"/> (D47): the company's latest version,
/// else every company's, in the language asked for, else in the default language; else the built-in text. The
/// built-in disclosure has the AI clause when AI CV analysis is on (docs/domain/yapay-zeka.md §10.3).
/// </summary>
public sealed record GetCurrentConsentTextQuery(ConsentKind Kind, Guid? CompanyId = null, string? Culture = null) : IQuery<ConsentTextItem>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Settings.Read;
}

/// <summary>
/// Publishes a new version of a KVKK text for a company (or every company, Tenant scope) and a language; earlier
/// versions stay as they were. Returns its id.
/// </summary>
public sealed record PublishConsentTextCommand(ConsentKind Kind, Guid? CompanyId, string? Culture, string? Body) : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

internal sealed class PublishConsentTextCommandValidator : AbstractValidator<PublishConsentTextCommand>
{
    public PublishConsentTextCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Culture).NotEmpty().MaximumLength(ConsentText.CultureMaxLength).Matches("^[a-z]{2}-[A-Z]{2}$").WithErrorCode(CommonErrors.InvalidCulture.Code);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(ConsentText.BodyMaxLength);
    }
}

internal sealed class GetConsentTextsQueryHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetConsentTextsQuery, IReadOnlyList<ConsentTextItem>>
{
    public async Task<Result<IReadOnlyList<ConsentTextItem>>> Handle(GetConsentTextsQuery request, CancellationToken cancellationToken)
    {
        var companies = await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken);
        var texts = await db.ConsentTexts.AsNoTracking()
            .Where(t => (request.Kind == null || t.Kind == request.Kind) && (request.CompanyId == null || t.CompanyId == request.CompanyId))
            .OrderByDescending(t => t.PublishedAt)
            .ToListAsync(cancellationToken);
        return texts
            .Where(t => companies is null || t.CompanyId is not { } id || companies.Contains(id))
            .Select(t => new ConsentTextItem(t.Id.Value, t.Kind, t.CompanyId, t.Culture, t.Revision, t.Body, t.PublishedAt))
            .ToList();
    }
}

internal sealed class GetCurrentConsentTextQueryHandler(IRecruitmentDbContext db, ILocalizationSource localization, ITextLocalizer localizer, IAiService ai)
    : IQueryHandler<GetCurrentConsentTextQuery, ConsentTextItem>
{
    public async Task<Result<ConsentTextItem>> Handle(GetCurrentConsentTextQuery request, CancellationToken cancellationToken)
    {
        var fallback = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        var culture = string.IsNullOrWhiteSpace(request.Culture) ? fallback : request.Culture;
        var candidates = await db.ConsentTexts.AsNoTracking()
            .Where(t => t.Kind == request.Kind && (t.CompanyId == request.CompanyId || t.CompanyId == null) && (t.Culture == culture || t.Culture == fallback))
            .ToListAsync(cancellationToken);

        // The language first (a candidate must understand it), then the company's own, then the latest version
        var current = candidates
            .OrderByDescending(t => string.Equals(t.Culture, culture, StringComparison.Ordinal))
            .ThenByDescending(t => t.CompanyId is not null)
            .ThenByDescending(t => t.Revision)
            .FirstOrDefault();
        if (current is not null)
        {
            return new ConsentTextItem(current.Id.Value, current.Kind, current.CompanyId, current.Culture, current.Revision, current.Body, current.PublishedAt);
        }

        var key = request.Kind == ConsentKind.Disclosure ? "recruitment.consents.defaults.disclosure" : "recruitment.consents.defaults.explicit_consent";
        var body = localizer.Get(key, culture: culture);
        if (request.Kind == ConsentKind.Disclosure && ai.IsEnabled(RecruitmentAiFeatures.CvAnalysis))
        {
            body += "\n\n" + localizer.Get("recruitment.consents.defaults.ai_clause", culture: culture);
        }

        return new ConsentTextItem(null, request.Kind, request.CompanyId, culture, 0, body, null);
    }
}

internal sealed class PublishConsentTextCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, TimeProvider timeProvider)
    : ICommandHandler<PublishConsentTextCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PublishConsentTextCommand request, CancellationToken cancellationToken)
    {
        var companies = await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken);
        if (request.CompanyId is not { } companyId ? companies is not null : companies is not null && !companies.Contains(companyId))
        {
            return request.CompanyId is null ? RecruitmentSettingsErrors.TenantScopeRequired : CommonErrors.Forbidden;
        }

        var culture = request.Culture!;
        var previous = await db.ConsentTexts
            .Where(t => t.Kind == request.Kind && t.CompanyId == request.CompanyId && t.Culture == culture)
            .MaxAsync(t => (int?)t.Revision, cancellationToken) ?? 0;
        var text = ConsentText.Publish(request.Kind, request.CompanyId, culture, previous, request.Body!, timeProvider.GetUtcNow());
        db.ConsentTexts.Add(text);
        return text.Id.Value;
    }
}
