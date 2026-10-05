using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Roles;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Users;

/// <summary>Users of the tenant, filtered and paged.</summary>
/// <remarks>Search matches part of the user name, e-mail address or name; RoleId keeps the holders of a role.</remarks>
public sealed record GetUsersQuery(PageRequest Paging, string? Search = null, bool? IsActive = null, Guid? RoleId = null)
    : IQuery<PagedResult<UserListItem>>, IRequirePermission
{
    public const string DefaultSort = "userName";

    public static readonly SortMap<User> Sorts = SortMap<User>.By(u => u.Id)
        .Add("userName", u => u.UserName)
        .Add("email", u => u.Email)
        .Add("firstName", u => u.FirstName)
        .Add("lastName", u => u.LastName)
        .Add("isActive", u => u.IsActive)
        .Add("createdAt", u => u.CreatedAt);

    public string Permission => PlatformPermissions.Users.Read;
}

/// <remarks>Roles lists the names of the roles in force today.</remarks>
public sealed record UserListItem(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Email,
    bool IsActive,
    Guid? EmployeeId,
    IReadOnlyList<string> Roles);

public sealed record GetUserQuery(Guid Id) : IQuery<UserDetails>, IRequirePermission
{
    public string Permission => PlatformPermissions.Users.Read;
}

public sealed record UserDetails(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    Guid? EmployeeId,
    string? Culture,
    IReadOnlyList<AssignmentItem> Assignments,
    DateTimeOffset CreatedAt);

/// <remarks>
/// ScopeName is the company or branch name for Platform scopes and null for scopes of other modules; IsCurrent is
/// true when the assignment is in force today.
/// </remarks>
public sealed record AssignmentItem(
    Guid Id,
    Guid RoleId,
    string RoleName,
    string ScopeType,
    string ScopeTypeLabel,
    Guid? ScopeId,
    string? ScopeName,
    bool IncludeSubUnits,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsCurrent);

internal sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator() =>
        RuleFor(q => q.Paging.Sort).Must(GetUsersQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
}

internal sealed class GetUsersQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, TimeProvider timeProvider)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            users = users.Where(u => TextSearch.Matches(u.UserName, pattern)
                || TextSearch.Matches(u.Email, pattern)
                || TextSearch.Matches(u.FirstName, pattern)
                || TextSearch.Matches(u.LastName, pattern));
        }

        if (request.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        if (request.RoleId is { } role)
        {
            var roleId = RoleId.From(role);
            users = users.Where(u => u.Assignments.Any(a => a.RoleId == roleId));
        }

        var page = await GetUsersQuery.Sorts.Apply(users, request.Paging.Sort, GetUsersQuery.DefaultSort)
            .Include(u => u.Assignments)
            .ToPagedResultAsync(request.Paging, cancellationToken);

        var today = PlatformClock.Today(timeProvider);
        var names = await RoleNames.LoadAsync(db, currentUser.Culture, cancellationToken);
        return new PagedResult<UserListItem>(
            [.. page.Items.Select(u => new UserListItem(
                u.Id.Value,
                u.UserName,
                u.DisplayName,
                u.Email,
                u.IsActive,
                u.EmployeeId,
                [.. u.Assignments.Where(a => a.IsValidOn(today)).Select(a => names.GetValueOrDefault(a.RoleId, "?")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetUserQueryHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    IScopeTypeRegistry scopeTypes,
    ITextLocalizer localizer,
    TimeProvider timeProvider) : IQueryHandler<GetUserQuery, UserDetails>
{
    public async Task<Result<UserDetails>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var user = await db.Users.AsNoTracking().Include(u => u.Assignments).SingleOrDefaultAsync(u => u.Id == UserId.From(request.Id), cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        var today = PlatformClock.Today(timeProvider);
        var roles = await RoleNames.LoadAsync(db, currentUser.Culture, cancellationToken);
        var scopeNames = await ScopeNamesAsync(user.Assignments, cancellationToken);
        return new UserDetails(
            user.Id.Value,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.EmployeeId,
            user.Culture,
            [.. user.Assignments
                .OrderBy(a => roles.GetValueOrDefault(a.RoleId), StringComparer.Ordinal)
                .ThenBy(a => a.ValidFrom)
                .Select(a => new AssignmentItem(
                    a.Id.Value,
                    a.RoleId.Value,
                    roles.GetValueOrDefault(a.RoleId, "?"),
                    a.ScopeType,
                    scopeTypes.Find(a.ScopeType) is { } type ? localizer.Get(type.LabelKey) : a.ScopeType,
                    a.ScopeId,
                    a.ScopeId is { } id ? scopeNames.GetValueOrDefault(id) : null,
                    a.IncludeSubUnits,
                    a.ValidFrom,
                    a.ValidTo,
                    a.IsValidOn(today)))],
            user.CreatedAt);
    }

    private async Task<Dictionary<Guid, string>> ScopeNamesAsync(IEnumerable<RoleAssignment> assignments, CancellationToken cancellationToken)
    {
        var list = assignments.ToList();
        var companyIds = list.Where(a => a.ScopeType == AccessScopeTypes.Company && a.ScopeId is not null).Select(a => CompanyId.From(a.ScopeId!.Value)).ToList();
        var branchIds = list.Where(a => a.ScopeType == AccessScopeTypes.Branch && a.ScopeId is not null).Select(a => BranchId.From(a.ScopeId!.Value)).ToList();
        var names = new Dictionary<Guid, string>();
        foreach (var company in await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).Select(c => new { c.Id, c.ShortName }).ToListAsync(cancellationToken))
        {
            names[company.Id.Value] = company.ShortName;
        }

        foreach (var branch in await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).Select(b => new { b.Id, b.Name }).ToListAsync(cancellationToken))
        {
            names[branch.Id.Value] = branch.Name;
        }

        return names;
    }
}

internal static class RoleNames
{
    /// <summary>Role names in <paramref name="culture"/>, including deleted roles so old assignments still read well.</summary>
    public static async Task<Dictionary<RoleId, string>> LoadAsync(IPlatformDbContext db, string culture, CancellationToken cancellationToken) =>
        (await db.Roles.AsNoTracking().IgnoreQueryFilters().Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken))
        .ToDictionary(r => r.Id, r => r.Name.Get(culture, "tr-TR"));
}
