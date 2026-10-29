using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Settings;

/// <summary>An interview evaluation template; <c>Version</c> is sent back when updating.</summary>
public sealed record EvaluationTemplateItem(Guid Id, string Name, EvaluationForm Form, bool IsActive, uint Version);

/// <summary>The tenant's evaluation templates by name (D52).</summary>
public sealed record GetEvaluationTemplatesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<EvaluationTemplateItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Settings.Read;
}

/// <summary>Adds an evaluation template; returns its id. Needs the Tenant scope.</summary>
public sealed record CreateEvaluationTemplateCommand(string? Name, EvaluationForm? Form) : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

/// <summary>Changes an evaluation template; interviews planned with it keep their copy. Needs the Tenant scope.</summary>
public sealed record UpdateEvaluationTemplateCommand(uint Version, string? Name, EvaluationForm? Form, bool IsActive) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

internal sealed class CreateEvaluationTemplateCommandValidator : AbstractValidator<CreateEvaluationTemplateCommand>
{
    public CreateEvaluationTemplateCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(EvaluationTemplate.NameMaxLength);
        RuleFor(c => c.Form).NotNull().SetValidator(new EvaluationFormValidator()!);
    }
}

internal sealed class UpdateEvaluationTemplateCommandValidator : AbstractValidator<UpdateEvaluationTemplateCommand>
{
    public UpdateEvaluationTemplateCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(EvaluationTemplate.NameMaxLength);
        RuleFor(c => c.Form).NotNull().SetValidator(new EvaluationFormValidator()!);
    }
}

/// <summary>Lengths and counts of a form; the domain checks the rest (D52).</summary>
internal sealed class EvaluationFormValidator : AbstractValidator<EvaluationForm>
{
    public EvaluationFormValidator()
    {
        RuleFor(f => f.Sections).NotEmpty();
        RuleForEach(f => f.Sections).ChildRules(section =>
        {
            section.RuleFor(s => s.Title).NotEmpty().MaximumLength(EvaluationSection.TitleMaxLength);
            section.RuleFor(s => s.Criteria).NotEmpty();
            section.RuleForEach(s => s.Criteria).ChildRules(criterion =>
            {
                criterion.RuleFor(c => c.Name).NotEmpty().MaximumLength(EvaluationCriterion.NameMaxLength);
                criterion.RuleFor(c => c.ScoreDescriptions).Must(d => d is { Count: EvaluationForm.Scores }).WithErrorCode(CommonErrors.ValidationCode);
                criterion.RuleForEach(c => c.ScoreDescriptions).MaximumLength(EvaluationCriterion.DescriptionMaxLength);
                criterion.RuleFor(c => c.Weight).GreaterThan(0).LessThanOrEqualTo(EvaluationCriterion.MaxWeight);
            });
        });
        RuleFor(f => f.Questions).Must(q => q is null || q.Count <= EvaluationForm.MaxQuestions).WithErrorCode(CommonErrors.ValidationCode);
        RuleForEach(f => f.Questions).ChildRules(question => question.RuleFor(q => q.Text).NotEmpty().MaximumLength(EvaluationQuestion.TextMaxLength));
    }
}

internal sealed class GetEvaluationTemplatesQueryHandler(IRecruitmentDbContext db) : IQueryHandler<GetEvaluationTemplatesQuery, IReadOnlyList<EvaluationTemplateItem>>
{
    public async Task<Result<IReadOnlyList<EvaluationTemplateItem>>> Handle(GetEvaluationTemplatesQuery request, CancellationToken cancellationToken) =>
        (await db.EvaluationTemplates.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new { Template = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken))
        .Select(t => new EvaluationTemplateItem(t.Template.Id.Value, t.Template.Name, t.Template.Form, t.Template.IsActive, t.Version))
        .ToList();
}

internal sealed class CreateEvaluationTemplateCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<CreateEvaluationTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEvaluationTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        var template = EvaluationTemplate.Create(request.Name!, request.Form!);
        if (template.IsFailure)
        {
            return template.Error;
        }

        db.EvaluationTemplates.Add(template.Value);
        return template.Value.Id.Value;
    }
}

internal sealed class UpdateEvaluationTemplateCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateEvaluationTemplateCommand>
{
    public async Task<Result> Handle(UpdateEvaluationTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        var template = await db.EvaluationTemplates.SingleOrDefaultAsync(t => t.Id == EvaluationTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(template).ExpectVersion(request.Version);
        return template.Update(request.Name!, request.Form!, request.IsActive);
    }
}
