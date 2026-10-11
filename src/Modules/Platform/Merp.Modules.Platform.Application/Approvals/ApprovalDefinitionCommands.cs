using Merp.Application.Abstractions.Approvals;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>A resolver key and its parameters as entered; empty values are dropped.</summary>
public sealed record ApproverRuleInput(string? Resolver, IReadOnlyDictionary<string, string?>? Parameters);

/// <summary>A step as entered; Condition is the JSON rule tree of §2.3.</summary>
public sealed record ApprovalStepInput(
    IReadOnlyDictionary<string, string?>? Name,
    ApproverRuleInput? Approver,
    ApprovalMode Mode,
    JsonElement? Condition,
    int? DueHours,
    ApprovalEscalation Escalation,
    bool SkipIfApproverIsRequester = true,
    bool SkipIfAlreadyApproved = true,
    bool AllowReturn = false);

/// <summary>What may change after creation; the document type and company never change.</summary>
public interface IApprovalDefinitionInput
{
    IReadOnlyDictionary<string, string?>? Name { get; }

    int Priority { get; }

    JsonElement? EntryCondition { get; }

    IReadOnlyList<ApprovalStepInput>? Steps { get; }

    ApproverRuleInput? Fallback { get; }
}

/// <summary>Defines an approval flow of a document type, for every company or one (§2.1, §2.2).</summary>
public sealed record CreateApprovalDefinitionCommand(
    string? DocumentType,
    Guid? CompanyId,
    IReadOnlyDictionary<string, string?>? Name,
    int Priority,
    JsonElement? EntryCondition,
    IReadOnlyList<ApprovalStepInput>? Steps,
    ApproverRuleInput? Fallback) : ICommand<Guid>, IRequirePermission, IApprovalDefinitionInput
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Manage;
}

/// <summary>Changes a flow; running requests keep the steps they copied (§2.2).</summary>
public sealed record UpdateApprovalDefinitionCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    int Priority,
    JsonElement? EntryCondition,
    IReadOnlyList<ApprovalStepInput>? Steps,
    ApproverRuleInput? Fallback,
    bool IsActive) : ICommand, IRequirePermission, IApprovalDefinitionInput
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.ApprovalDefinitions.Manage;
}

public static class ApprovalDefinitionErrors
{
    public const string UnknownCompanyCode = "platform.approvals.errors.unknown_company";
    public const string UnknownResolverCode = "platform.approvals.errors.unknown_resolver";
    public const string MissingParameterCode = "platform.approvals.errors.missing_parameter";
    public const string TooManyStepsCode = "platform.approvals.errors.too_many_steps";
    public const string DueHoursRequiredCode = "platform.approvals.errors.due_hours_required";

    /// <summary>A year; longer deadlines are mistakes.</summary>
    public const int MaxDueHours = 8760;
}

internal static class ApprovalDefinitionRules
{
    public static void AddInputRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : IApprovalDefinitionInput
    {
        validator.RuleFor(c => c.Name).NotEmpty().MustCoverDefaultCulture(localization);
        validator.RuleFor(c => c.Steps).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(steps => steps!.Count <= ApprovalDefinition.MaxSteps).WithErrorCode(ApprovalDefinitionErrors.TooManyStepsCode);
        validator.RuleForEach(c => c.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.Name).NotEmpty().MustCoverDefaultCulture(localization);
            step.RuleFor(s => s.Approver).NotNull();
            step.RuleFor(s => s.Mode).IsInEnum();
            step.RuleFor(s => s.Escalation).IsInEnum();
            step.RuleFor(s => s.DueHours).InclusiveBetween(1, ApprovalDefinitionErrors.MaxDueHours);
            step.RuleFor(s => s.DueHours).NotNull().When(s => s.Escalation != ApprovalEscalation.None)
                .WithErrorCode(ApprovalDefinitionErrors.DueHoursRequiredCode);
        });
    }

    /// <summary>
    /// Checks what needs the registries (§2.3, §2.4): conditions against the document type's facts, resolvers and
    /// their required parameters. Returns the domain steps, or the errors with their field paths.
    /// </summary>
    public static Result<(IReadOnlyList<ApprovalStep> Steps, string? EntryCondition, ApproverRule? Fallback)> Build(
        IApprovalDefinitionInput input,
        ApprovalDocumentType type,
        ApproverResolverRegistry resolvers)
    {
        var errors = new List<FieldError>();
        var entryCondition = Condition(input.EntryCondition, type, "entryCondition", errors);
        var steps = new List<ApprovalStep>();
        for (var i = 0; i < input.Steps!.Count; i++)
        {
            var step = input.Steps[i];
            var path = string.Create(CultureInfo.InvariantCulture, $"steps[{i}]");
            var approver = Rule(step.Approver!, resolvers, path + ".approver", errors);
            var condition = Condition(step.Condition, type, path + ".condition", errors);
            if (approver is not null)
            {
                steps.Add(new ApprovalStep(
                    LocalizedString.Create(step.Name).Value.Values,
                    approver,
                    step.Mode,
                    condition,
                    step.DueHours,
                    step.Escalation,
                    step.SkipIfApproverIsRequester,
                    step.SkipIfAlreadyApproved,
                    step.AllowReturn));
            }
        }

        var fallback = input.Fallback is { } given ? Rule(given, resolvers, "fallback", errors) : null;
        return errors.Count > 0 ? Error.Validation(errors) : (steps, entryCondition, fallback);
    }

    private static string? Condition(JsonElement? input, ApprovalDocumentType type, string field, List<FieldError> errors)
    {
        if (input is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } condition)
        {
            return null;
        }

        var json = condition.GetRawText();
        var parsed = ApprovalConditions.Parse(json, type.Facts, field);
        if (parsed.IsFailure)
        {
            errors.AddRange(parsed.Error.Fields);
            return null;
        }

        return json;
    }

    private static ApproverRule? Rule(ApproverRuleInput input, ApproverResolverRegistry resolvers, string field, List<FieldError> errors)
    {
        if (resolvers.Find(input.Resolver?.Trim()) is not { } resolver)
        {
            errors.Add(new FieldError(field + ".resolver", ApprovalDefinitionErrors.UnknownResolverCode));
            return null;
        }

        var parameters = (input.Parameters ?? new Dictionary<string, string?>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Value) && resolver.Parameters.Any(known => known.Name == p.Key))
            .ToDictionary(p => p.Key, p => p.Value!.Trim(), StringComparer.Ordinal);
        var missing = resolver.Parameters.Where(p => p.Required && !parameters.ContainsKey(p.Name)).ToList();
        foreach (var parameter in missing)
        {
            errors.Add(new FieldError($"{field}.parameters.{parameter.Name}", ApprovalDefinitionErrors.MissingParameterCode));
        }

        return missing.Count == 0 ? new ApproverRule(resolver.Key, parameters) : null;
    }
}

internal sealed class CreateApprovalDefinitionCommandValidator : AbstractValidator<CreateApprovalDefinitionCommand>
{
    public CreateApprovalDefinitionCommandValidator(ILocalizationSource localization)
    {
        RuleFor(c => c.DocumentType).NotEmpty();
        ApprovalDefinitionRules.AddInputRules(this, localization);
    }
}

internal sealed class UpdateApprovalDefinitionCommandValidator : AbstractValidator<UpdateApprovalDefinitionCommand>
{
    public UpdateApprovalDefinitionCommandValidator(ILocalizationSource localization) => ApprovalDefinitionRules.AddInputRules(this, localization);
}

internal sealed class CreateApprovalDefinitionCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ApprovalDocumentTypeRegistry documentTypes,
    ApproverResolverRegistry resolvers) : ICommandHandler<CreateApprovalDefinitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateApprovalDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var errors = new List<FieldError>();
        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            errors.Add(new FieldError("companyId", ApprovalDefinitionErrors.UnknownCompanyCode));
        }

        if (documentTypes.Find(request.DocumentType!.Trim()) is not { } type)
        {
            errors.Add(new FieldError("documentType", ApprovalErrors.UnknownDocumentTypeCode));
            return Error.Validation(errors);
        }

        var built = ApprovalDefinitionRules.Build(request, type, resolvers);
        if (built.IsFailure)
        {
            errors.AddRange(built.Error.Fields);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var (steps, entryCondition, fallback) = built.Value;
        var definition = ApprovalDefinition.Create(type.Key, request.CompanyId, LocalizedString.Create(request.Name).Value, request.Priority, entryCondition, steps, fallback);
        db.ApprovalDefinitions.Add(definition);
        return definition.Id.Value;
    }
}

internal sealed class UpdateApprovalDefinitionCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ApprovalDocumentTypeRegistry documentTypes,
    ApproverResolverRegistry resolvers) : ICommandHandler<UpdateApprovalDefinitionCommand>
{
    public async Task<Result> Handle(UpdateApprovalDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return ApprovalErrors.TenantScopeRequired;
        }

        var definition = await db.ApprovalDefinitions.SingleOrDefaultAsync(d => d.Id == ApprovalDefinitionId.From(request.Id), cancellationToken);
        if (definition is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(definition).ExpectVersion(request.Version);

        // A module that no longer registers the type leaves its definitions read-only
        if (documentTypes.Find(definition.DocumentType) is not { } type)
        {
            return Error.Validation([new FieldError("documentType", ApprovalErrors.UnknownDocumentTypeCode)]);
        }

        var built = ApprovalDefinitionRules.Build(request, type, resolvers);
        if (built.IsFailure)
        {
            return built.Error;
        }

        var (steps, entryCondition, fallback) = built.Value;
        definition.Change(LocalizedString.Create(request.Name).Value, request.Priority, entryCondition, steps, fallback, request.IsActive);
        return Result.Success();
    }
}
