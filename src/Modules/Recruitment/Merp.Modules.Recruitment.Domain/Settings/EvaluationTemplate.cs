using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Settings;

public readonly record struct EvaluationTemplateId(Guid Value) : IEntityId<EvaluationTemplateId>
{
    public static EvaluationTemplateId From(Guid value) => new(value);
}

/// <summary>
/// An interview evaluation template of the tenant (değerlendirme şablonu, PLAN R5, D52): the form interviewers fill
/// in. An interview copies the form, so changes do not reach interviews already planned.
/// </summary>
[Audited]
public sealed class EvaluationTemplate : AuditableAggregateRoot<EvaluationTemplateId>
{
    public const int NameMaxLength = 150;

    private EvaluationTemplate()
    {
    }

    public string Name { get; private set; } = null!;

    public EvaluationForm Form { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static Result<EvaluationTemplate> Create(string name, EvaluationForm form)
    {
        var template = new EvaluationTemplate { Id = EntityId.New<EvaluationTemplateId>(), IsActive = true };
        var set = template.Update(name, form, isActive: true);
        return set.IsFailure ? set.Error : template;
    }

    public Result Update(string name, EvaluationForm form, bool isActive)
    {
        var checkedForm = EvaluationForm.Check(form);
        if (checkedForm.IsFailure)
        {
            return checkedForm.Error;
        }

        Name = Text.Required(name, NameMaxLength, nameof(name));
        Form = checkedForm.Value;
        IsActive = isActive;
        return Result.Success();
    }
}
