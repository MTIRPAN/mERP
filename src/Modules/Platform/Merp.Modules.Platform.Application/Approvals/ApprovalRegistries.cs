using Merp.Application.Abstractions.Approvals;
using System.Text.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>The approvable document types of all modules (§2.1).</summary>
public sealed class ApprovalDocumentTypeRegistry(IEnumerable<IApprovalDocumentTypeProvider> providers)
{
    private readonly Dictionary<string, ApprovalDocumentType> _types =
        providers.SelectMany(p => p.GetDocumentTypes()).ToDictionary(t => t.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<ApprovalDocumentType> All => _types.Values;

    public ApprovalDocumentType? Find(string? key) => key is not null && _types.TryGetValue(key, out var type) ? type : null;
}

/// <summary>The approver resolvers of all modules (§2.4).</summary>
public sealed class ApproverResolverRegistry(IEnumerable<IApproverResolver> resolvers)
{
    private readonly Dictionary<string, IApproverResolver> _resolvers = resolvers.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<IApproverResolver> All => _resolvers.Values;

    public IApproverResolver? Find(string? key) => key is not null && _resolvers.TryGetValue(key, out var resolver) ? resolver : null;
}

/// <summary>A user named in the definition (<c>user</c>, §2.4).</summary>
public sealed class UserApproverResolver(IPlatformDbContext db) : IApproverResolver
{
    public const string ResolverKey = "user";
    public const string UserIdParameter = "userId";

    public string Key => ResolverKey;

    public string NameKey => "platform.approvals.resolvers.user";

    public IReadOnlyList<ApproverParameter> Parameters { get; } = [new(UserIdParameter, "platform.approvals.resolvers.user.user_id")];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(ApproverResolutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.TryGetValue(UserIdParameter, out var value) && Guid.TryParse(value, out var userId)
            ? await ActiveUsers.FilterAsync(db, [userId], cancellationToken)
            : [];
    }
}

/// <summary>
/// Holders of a role whose assignment, valid today, covers the document (<c>role</c>, §2.4): a Tenant scope covers
/// every document, a Company scope the company's, a Branch or OrgUnit scope the documents whose <c>branchId</c> or
/// <c>orgUnitId</c> fact names it. Sub-units are not followed: the unit tree belongs to HR.
/// </summary>
public sealed class RoleApproverResolver(IPlatformDbContext db, TimeProvider timeProvider) : IApproverResolver
{
    public const string ResolverKey = "role";
    public const string RoleIdParameter = "roleId";

    public string Key => ResolverKey;

    public string NameKey => "platform.approvals.resolvers.role";

    public IReadOnlyList<ApproverParameter> Parameters { get; } = [new(RoleIdParameter, "platform.approvals.resolvers.role.role_id")];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(ApproverResolutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(RoleIdParameter, out var value) || !Guid.TryParse(value, out var id))
        {
            return [];
        }

        var roleId = RoleId.From(id);
        var today = PlatformClock.Today(timeProvider);
        var companyId = context.CompanyId;
        var branchId = FactGuid(context.Facts, "branchId");
        var orgUnitId = FactGuid(context.Facts, "orgUnitId");
        return await db.Users
            .Where(u => u.IsActive && u.Assignments.Any(a =>
                a.RoleId == roleId
                && (a.ValidFrom == null || a.ValidFrom <= today)
                && (a.ValidTo == null || a.ValidTo >= today)
                && (a.ScopeType == AccessScopeTypes.Tenant
                    || (a.ScopeType == AccessScopeTypes.Company && companyId != null && a.ScopeId == companyId)
                    || (a.ScopeType == AccessScopeTypes.Branch && branchId != null && a.ScopeId == branchId)
                    || (a.ScopeType == AccessScopeTypes.OrgUnit && orgUnitId != null && a.ScopeId == orgUnitId))))
            .OrderBy(u => u.UserName)
            .Select(u => u.Id.Value)
            .ToListAsync(cancellationToken);
    }

    private static Guid? FactGuid(JsonElement facts, string name) =>
        facts.ValueKind == JsonValueKind.Object
        && facts.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;
}

/// <summary>The user whose id a fact holds, e.g. <c>hiringManagerUserId</c> (<c>fact-user</c>, §2.4).</summary>
public sealed class FactUserApproverResolver(IPlatformDbContext db) : IApproverResolver
{
    public const string ResolverKey = "fact-user";
    public const string FactNameParameter = "factName";

    public string Key => ResolverKey;

    public string NameKey => "platform.approvals.resolvers.fact_user";

    public IReadOnlyList<ApproverParameter> Parameters { get; } = [new(FactNameParameter, "platform.approvals.resolvers.fact_user.fact_name")];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(ApproverResolutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(FactNameParameter, out var fact)
            || context.Facts.ValueKind != JsonValueKind.Object
            || !context.Facts.TryGetProperty(fact, out var value)
            || value.ValueKind != JsonValueKind.String
            || !Guid.TryParse(value.GetString(), out var userId))
        {
            return [];
        }

        return await ActiveUsers.FilterAsync(db, [userId], cancellationToken);
    }
}

internal static class ActiveUsers
{
    /// <summary>The given users that exist and are active, in the given order.</summary>
    public static async Task<IReadOnlyList<Guid>> FilterAsync(IPlatformDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.Select(UserId.From).ToList();
        var active = await db.Users.Where(u => ids.Contains(u.Id) && u.IsActive).Select(u => u.Id.Value).ToListAsync(cancellationToken);
        return [.. userIds.Distinct().Where(active.Contains)];
    }
}
