using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Localization;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Approvals;

/// <summary>
/// Seeds the default flows of the document types (docs/domain/yetki-ve-onay.md §2.8, D34) as general definitions,
/// once per type: a type that has any definition, active or not, is left alone, so a tenant's changes stay. Runs
/// after the system roles are seeded; a <c>role</c> step's system role code becomes the tenant's role id.
/// </summary>
internal sealed partial class ApprovalDefinitionSeeder(ILogger<ApprovalDefinitionSeeder> logger) : ITenantDataSeeder
{
    private const string Culture = "tr-TR";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PlatformDbContext>();
        var types = services.GetRequiredService<ApprovalDocumentTypeRegistry>();
        var localizer = services.GetRequiredService<ITextLocalizer>();

        foreach (var type in types.All.Where(t => t.DefaultFlow is { Count: > 0 }))
        {
            if (await db.ApprovalDefinitions.AnyAsync(d => d.DocumentType == type.Key, cancellationToken))
            {
                continue;
            }

            var steps = new List<ApprovalStep>();
            foreach (var step in type.DefaultFlow!)
            {
                if (await RuleAsync(db, step, cancellationToken) is not { } rule)
                {
                    LogUnknownRole(logger, type.Key, step.Parameters.GetValueOrDefault(ApprovalDefaultStep.RoleCodeParameter));
                    steps.Clear();
                    break;
                }

                if (ApprovalConditions.Parse(step.Condition, type.Facts, "condition").IsFailure)
                {
                    throw new InvalidOperationException($"The default flow of {type.Key} has a condition that does not fit its facts.");
                }

                steps.Add(new ApprovalStep(Name(localizer, step.NameKey).Values, rule, Condition: step.Condition));
            }

            if (steps.Count > 0)
            {
                db.ApprovalDefinitions.Add(ApprovalDefinition.Create(type.Key, null, Name(localizer, type.NameKey), 0, null, steps, null));
                LogSeeded(logger, type.Key, steps.Count);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ApproverRule?> RuleAsync(PlatformDbContext db, ApprovalDefaultStep step, CancellationToken cancellationToken)
    {
        var parameters = step.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (!parameters.Remove(ApprovalDefaultStep.RoleCodeParameter, out var roleCode))
        {
            return new ApproverRule(step.Resolver, parameters);
        }

        var code = Code.Create(roleCode, Role.CodeMaxLength);
        if (code.IsFailure)
        {
            return null;
        }

        var role = await db.Roles.AsNoTracking().Where(r => r.Code == code.Value.Value).Select(r => r.Id).SingleOrDefaultAsync(cancellationToken);
        if (role == default)
        {
            return null;
        }

        parameters[RoleApproverResolver.RoleIdParameter] = role.Value.ToString();
        return new ApproverRule(step.Resolver, parameters);
    }

    private static LocalizedString Name(ITextLocalizer localizer, string key) => LocalizedString.Of(Culture, localizer.Get(key, culture: Culture));

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the default approval flow of {DocumentType} with {StepCount} steps")]
    private static partial void LogSeeded(ILogger logger, string documentType, int stepCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The default approval flow of {DocumentType} names the unknown role {RoleCode}; it is not seeded")]
    private static partial void LogUnknownRole(ILogger logger, string documentType, string? roleCode);
}
