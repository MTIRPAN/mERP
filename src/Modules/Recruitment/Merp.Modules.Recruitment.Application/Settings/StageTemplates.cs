using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Settings;

/// <summary>The stage templates, the default first (D47).</summary>
public sealed record GetStageTemplatesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<StageTemplateItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Settings.Read;
}

/// <remarks>Name is in the user's language, Names in each language for the form; Version is sent back when updating.</remarks>
public sealed record StageTemplateItem(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Names,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<StageItem> Stages,
    uint Version);

public sealed record StageItem(Guid Id, string Name, IReadOnlyDictionary<string, string> Names, StageType Type, int? SlaDays);

/// <summary>A stage as the form sends it; a new stage has no id.</summary>
public sealed record StageInput(Guid? Id, IReadOnlyDictionary<string, string?>? Name, StageType Type, int? SlaDays);

/// <summary>A pipeline as a form sends it: a template's, or an opening's own (D50).</summary>
public interface IStageListFields
{
    IReadOnlyList<StageInput>? Stages { get; }
}

public interface IStageTemplateFields : IStageListFields
{
    IReadOnlyDictionary<string, string?>? Name { get; }

    bool IsDefault { get; }
}

/// <summary>Adds a stage template; returns its id. Needs the Tenant scope.</summary>
public sealed record CreateStageTemplateCommand(IReadOnlyDictionary<string, string?>? Name, IReadOnlyList<StageInput>? Stages, bool IsDefault = false)
    : ICommand<Guid>, IRequirePermission, IStageTemplateFields
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

/// <summary>Changes a stage template; openings that copied it keep their copy (R3). Needs the Tenant scope.</summary>
public sealed record UpdateStageTemplateCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyList<StageInput>? Stages,
    bool IsDefault,
    bool IsActive) : ICommand, IRequirePermission, IStageTemplateFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

internal static class StageTemplateRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : IStageTemplateFields
    {
        RecruitmentSettingRules.AddNameRules(validator, t => t.Name, localization, StageTemplate.NameMaxLength);
        AddStageRules(validator, localization);
    }

    public static void AddStageRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : IStageListFields
    {
        validator.RuleFor(t => t.Stages).NotEmpty().Must(s => s!.Count <= StageTemplate.MaxStages).WithErrorCode(CommonErrors.ValidationCode);
        validator.RuleForEach(t => t.Stages).ChildRules(stage =>
        {
            RecruitmentSettingRules.AddNameRules(stage, s => s.Name, localization, Stage.NameMaxLength);
            stage.RuleFor(s => s.Type).IsInEnum();
            stage.RuleFor(s => s.SlaDays).InclusiveBetween(1, Stage.MaxSlaDays).When(s => s.SlaDays is not null);
        });
    }

    public static List<Stage> ToStages(IReadOnlyList<StageInput> stages) =>
        [.. stages.Select(s => new Stage(s.Id ?? Guid.Empty, LocalizedString.Create(s.Name).Value, s.Type, s.SlaDays))];
}

internal sealed class CreateStageTemplateCommandValidator : AbstractValidator<CreateStageTemplateCommand>
{
    public CreateStageTemplateCommandValidator(ILocalizationSource localization) => StageTemplateRules.AddFieldRules(this, localization);
}

internal sealed class UpdateStageTemplateCommandValidator : AbstractValidator<UpdateStageTemplateCommand>
{
    public UpdateStageTemplateCommandValidator(ILocalizationSource localization) => StageTemplateRules.AddFieldRules(this, localization);
}

internal sealed class GetStageTemplatesQueryHandler(IRecruitmentDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateItem>>
{
    public async Task<Result<IReadOnlyList<StageTemplateItem>>> Handle(GetStageTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = await db.StageTemplates.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .Select(t => new { Template = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        string Name(LocalizedString name) => name.Get(currentUser.Culture, "tr-TR");
        return templates
            .OrderByDescending(t => t.Template.IsDefault)
            .ThenBy(t => Name(t.Template.Name), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(currentUser.Culture), ignoreCase: true))
            .Select(t => new StageTemplateItem(
                t.Template.Id.Value,
                Name(t.Template.Name),
                t.Template.Name.Values,
                t.Template.IsDefault,
                t.Template.IsActive,
                [.. t.Template.Stages.Select(s => new StageItem(s.Id, Name(s.Name), s.Name.Values, s.Type, s.SlaDays))],
                t.Version))
            .ToList();
    }
}

internal sealed class CreateStageTemplateCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<CreateStageTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateStageTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        var created = StageTemplate.Create(LocalizedString.Create(request.Name).Value, StageTemplateRules.ToStages(request.Stages!), request.IsDefault);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.StageTemplates.Add(created.Value);
        return created.Value.Id.Value;
    }
}

internal sealed class UpdateStageTemplateCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<UpdateStageTemplateCommand>
{
    public async Task<Result> Handle(UpdateStageTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.StageTemplates.SingleOrDefaultAsync(t => t.Id == StageTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        db.Entry(template).ExpectVersion(request.Version);
        return template.Update(LocalizedString.Create(request.Name).Value, StageTemplateRules.ToStages(request.Stages!), request.IsDefault, request.IsActive);
    }
}

/// <summary>A new default takes the place of the previous one in the same transaction.</summary>
internal sealed class ClearPreviousDefaultStageTemplate(IRecruitmentDbContext db) : IDomainEventHandler<StageTemplateMadeDefault>
{
    public async Task Handle(StageTemplateMadeDefault domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        foreach (var previous in await db.StageTemplates.Where(t => t.IsDefault && t.Id != domainEvent.TemplateId).ToListAsync(cancellationToken))
        {
            previous.ClearDefault();
        }
    }
}
