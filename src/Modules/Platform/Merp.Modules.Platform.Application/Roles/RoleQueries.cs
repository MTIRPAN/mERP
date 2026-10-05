using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Domain.Roles;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Roles;

/// <summary>Roles of the tenant; a tenant has few roles, so they are filtered and paged in memory.</summary>
/// <remarks>Search matches part of the code or the name in the caller's culture.</remarks>
public sealed record GetRolesQuery(PageRequest Paging, string? Search = null) : IQuery<PagedResult<RoleListItem>>, IRequirePermission
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "permissionCount", "userCount"];

    public string Permission => PlatformPermissions.Roles.Read;
}

/// <remarks>UserCount counts users holding the role, whatever the validity dates.</remarks>
public sealed record RoleListItem(Guid Id, string Code, string Name, bool IsSystem, int PermissionCount, int UserCount);

public sealed record GetRoleQuery(Guid Id) : IQuery<RoleDetails>, IRequirePermission
{
    public string Permission => PlatformPermissions.Roles.Read;
}

/// <remarks>Name holds the text per culture; send Version back when updating.</remarks>
public sealed record RoleDetails(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount,
    uint Version);

/// <summary>The permission catalog as a tree (module → resource → permission) with localized labels.</summary>
public sealed record GetPermissionTreeQuery : IQuery<IReadOnlyList<PermissionModuleNode>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Roles.Read;
}

public sealed record PermissionModuleNode(string Code, string Label, IReadOnlyList<PermissionResourceNode> Resources);

public sealed record PermissionResourceNode(string Code, string Label, IReadOnlyList<PermissionNode> Permissions);

public sealed record PermissionNode(string Code, string Label);

/// <summary>Scope types a role assignment can use, for the scope picker.</summary>
public sealed record GetScopeTypesQuery : IQuery<IReadOnlyList<ScopeTypeItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Users.Read;
}

public sealed record ScopeTypeItem(string Code, string Label, bool RequiresId, bool SupportsSubUnits);

internal sealed class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator() =>
        RuleFor(q => q.Paging.Sort)
            .Must(sort => string.IsNullOrWhiteSpace(sort) || GetRolesQuery.SortFields.Contains(sort.Trim().TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithErrorCode(CommonErrors.InvalidSort.Code)
            .OverridePropertyName("sort");
}

internal sealed class GetRolesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
    : IQueryHandler<GetRolesQuery, PagedResult<RoleListItem>>
{
    public async Task<Result<PagedResult<RoleListItem>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var roles = await db.Roles.AsNoTracking()
            .Select(r => new { r.Id, r.Code, r.Name, r.IsSystem, Permissions = r.Permissions.Count })
            .ToListAsync(cancellationToken);
        var users = await RoleUsage.CountAsync(db, cancellationToken);

        var items = roles.Select(r => new RoleListItem(
            r.Id.Value,
            r.Code,
            r.Name.Get(currentUser.Culture, "tr-TR"),
            r.IsSystem,
            r.Permissions,
            users.GetValueOrDefault(r.Id)));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            items = items.Where(r => r.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        var sort = string.IsNullOrWhiteSpace(request.Paging.Sort) ? "name" : request.Paging.Sort.Trim();
        var descending = sort.StartsWith('-');
        var turkish = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);
        var ordered = sort.TrimStart('-').ToUpperInvariant() switch
        {
            "CODE" => descending ? items.OrderByDescending(r => r.Code, StringComparer.Ordinal) : items.OrderBy(r => r.Code, StringComparer.Ordinal),
            "PERMISSIONCOUNT" => descending ? items.OrderByDescending(r => r.PermissionCount) : items.OrderBy(r => r.PermissionCount),
            "USERCOUNT" => descending ? items.OrderByDescending(r => r.UserCount) : items.OrderBy(r => r.UserCount),
            _ => descending ? items.OrderByDescending(r => r.Name, turkish) : items.OrderBy(r => r.Name, turkish),
        };

        var list = ordered.ThenBy(r => r.Id).ToList();
        return new PagedResult<RoleListItem>(
            [.. list.Skip(request.Paging.Skip).Take(request.Paging.Size)],
            request.Paging.PageNumber,
            request.Paging.Size,
            list.Count);
    }
}

internal sealed class GetRoleQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes) : IQueryHandler<GetRoleQuery, RoleDetails>
{
    public async Task<Result<RoleDetails>> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = RoleId.From(request.Id);
        var row = await db.Roles.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { Role = r, Permissions = r.Permissions.Select(p => p.PermissionCode).ToList(), Version = EF.Property<uint>(r, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var users = await RoleUsage.CountAsync(db, cancellationToken);
        return new RoleDetails(
            row.Role.Id.Value,
            row.Role.Code,
            row.Role.Name.Values,
            row.Role.Description,
            row.Role.IsSystem,
            [.. row.Permissions.Order(StringComparer.Ordinal)],
            users.GetValueOrDefault(row.Role.Id),
            row.Version);
    }
}

internal static class RoleUsage
{
    /// <summary>Number of users per role.</summary>
    public static async Task<Dictionary<RoleId, int>> CountAsync(IPlatformDbContext db, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking()
            .SelectMany(u => u.Assignments.Select(a => new { a.RoleId, u.Id }))
            .Distinct()
            .ToListAsync(cancellationToken))
        .GroupBy(x => x.RoleId)
        .ToDictionary(g => g.Key, g => g.Count());
}

internal sealed class GetPermissionTreeQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ITextLocalizer localizer)
    : IQueryHandler<GetPermissionTreeQuery, IReadOnlyList<PermissionModuleNode>>
{
    public async Task<Result<IReadOnlyList<PermissionModuleNode>>> Handle(GetPermissionTreeQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var codes = await db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(cancellationToken);
        return codes
            .GroupBy(code => code.Split('.')[0], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(module => new PermissionModuleNode(
                module.Key,
                Label(module.Key),
                [.. module
                    .GroupBy(code => code[..code.LastIndexOf('.')], StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(resource => new PermissionResourceNode(
                        resource.Key,
                        Label(resource.Key),
                        [.. resource.Order(StringComparer.Ordinal).Select(code => new PermissionNode(code, Label(code)))]))]))
            .ToList();
    }

    private string Label(string code) => localizer.Get("permissions." + code);
}

internal sealed class GetScopeTypesQueryHandler(IScopeTypeRegistry registry, ITextLocalizer localizer)
    : IQueryHandler<GetScopeTypesQuery, IReadOnlyList<ScopeTypeItem>>
{
    public Task<Result<IReadOnlyList<ScopeTypeItem>>> Handle(GetScopeTypesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<ScopeTypeItem>>(
            [.. registry.All.Select(t => new ScopeTypeItem(t.Code, localizer.Get(t.LabelKey), t.RequiresId, t.SupportsSubUnits))]));
}
