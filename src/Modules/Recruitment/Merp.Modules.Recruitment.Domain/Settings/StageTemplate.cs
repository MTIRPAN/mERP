using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.Domain.Settings;

public readonly record struct StageTemplateId(Guid Value) : IEntityId<StageTemplateId>
{
    public static StageTemplateId From(Guid value) => new(value);
}

/// <summary>What a stage of the hiring pipeline is (PLAN R1); reports and rules follow the type, not the name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StageType>))]
public enum StageType
{
    Applied,
    Screening,
    Interview,
    Assessment,
    Reference,
    Offer,
    Hired,
    Rejected,
    Withdrawn,
}

/// <summary>A stage of a template; its id stays when the template changes, so openings copy it and keep track.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">The name in each language.</param>
/// <param name="Type">What the stage is.</param>
/// <param name="SlaDays">Days an application should stay in the stage at most; null for none.</param>
public sealed record Stage(Guid Id, LocalizedString Name, StageType Type, int? SlaDays)
{
    public const int NameMaxLength = 100;
    public const int MaxSlaDays = 365;

    /// <summary>The application leaves the pipeline in this stage.</summary>
    public bool IsFinal => Type is StageType.Hired or StageType.Rejected or StageType.Withdrawn;
}

/// <summary>
/// The stages of a hiring pipeline (aşama şablonu, PLAN R1, D47), in order. A job opening copies a template (R3).
/// Exactly one stage is Applied and comes first; Hired, Rejected and Withdrawn appear once each and come last; the
/// working stages between them are free. One template is the default of the tenant.
/// </summary>
[Audited]
public sealed class StageTemplate : AuditableAggregateRoot<StageTemplateId>
{
    public const int NameMaxLength = 100;
    public const int MaxStages = 20;

    private static readonly StageType[] Required = [StageType.Applied, StageType.Hired, StageType.Rejected, StageType.Withdrawn];

    private StageTemplate()
    {
    }

    public LocalizedString Name { get; private set; } = null!;

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<Stage> Stages { get; private set; } = [];

    public static Result<StageTemplate> Create(LocalizedString name, IReadOnlyList<Stage> stages, bool isDefault)
    {
        var checkedStages = Check(stages);
        if (checkedStages.IsFailure)
        {
            return checkedStages.Error;
        }

        var template = new StageTemplate
        {
            Id = EntityId.New<StageTemplateId>(),
            Name = name ?? throw new ArgumentNullException(nameof(name)),
            IsActive = true,
            Stages = checkedStages.Value,
        };
        template.SetDefault(isDefault);
        return template;
    }

    public Result Update(LocalizedString name, IReadOnlyList<Stage> stages, bool isDefault, bool isActive)
    {
        var checkedStages = Check(stages);
        if (checkedStages.IsFailure)
        {
            return checkedStages.Error;
        }

        if (isDefault && !isActive)
        {
            return StageTemplateErrors.DefaultInactive;
        }

        Name = name ?? throw new ArgumentNullException(nameof(name));
        Stages = checkedStages.Value;
        IsActive = isActive;
        SetDefault(isDefault);
        return Result.Success();
    }

    /// <summary>Another template became the default.</summary>
    public void ClearDefault() => IsDefault = false;

    /// <summary>
    /// The stages in order: Applied first, the working stages, then Hired, Rejected and Withdrawn; each stage id once.
    /// New stages (empty id) get one.
    /// </summary>
    private static Result<IReadOnlyList<Stage>> Check(IReadOnlyList<Stage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0 || stages.Count > MaxStages)
        {
            return StageTemplateErrors.Invalid;
        }

        if (Required.Any(type => stages.Count(s => s.Type == type) != 1) || stages[0].Type != StageType.Applied)
        {
            return StageTemplateErrors.RequiredStages;
        }

        // The final stages close the list, so the working stages run in one stretch
        var firstFinal = stages.ToList().FindIndex(s => s.IsFinal);
        if (stages.Skip(firstFinal).Any(s => !s.IsFinal))
        {
            return StageTemplateErrors.FinalStagesLast;
        }

        if (stages.Any(s => s.SlaDays is < 1 or > Stage.MaxSlaDays || (s.IsFinal && s.SlaDays is not null)))
        {
            return StageTemplateErrors.InvalidSla;
        }

        var withIds = stages.Select(s => s.Id == Guid.Empty ? s with { Id = Guid.CreateVersion7() } : s).ToList();
        if (withIds.Select(s => s.Id).Distinct().Count() != withIds.Count)
        {
            return StageTemplateErrors.Invalid;
        }

        return withIds;
    }

    private void SetDefault(bool isDefault)
    {
        if (isDefault && !IsDefault)
        {
            Raise(new StageTemplateMadeDefault(Id));
        }

        IsDefault = isDefault;
    }
}

public static class StageTemplateErrors
{
    public static readonly Error Invalid = Error.Validation([new FieldError("stages", "recruitment.settings.errors.invalid_stages")]);
    public static readonly Error RequiredStages = Error.Validation([new FieldError("stages", "recruitment.settings.errors.required_stages")]);
    public static readonly Error FinalStagesLast = Error.Validation([new FieldError("stages", "recruitment.settings.errors.final_stages_last")]);
    public static readonly Error InvalidSla = Error.Validation([new FieldError("stages", "recruitment.settings.errors.invalid_sla")]);
    public static readonly Error DefaultInactive = Error.Validation([new FieldError("isActive", "recruitment.settings.errors.default_inactive")]);
}

/// <summary>A template became the default: the previous default is not any more (same transaction).</summary>
public sealed record StageTemplateMadeDefault(StageTemplateId TemplateId) : IDomainEvent;
