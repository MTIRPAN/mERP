using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Branches;

/// <summary>
/// Branches in the caller's scope, filtered and paged. The active company (<c>X-Company-Id</c>, D4) narrows the
/// list further.
/// </summary>
/// <remarks>Search matches part of the code or name.</remarks>
public sealed record GetBranchesQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    string? Search = null,
    BranchType? Type = null,
    int? ProvinceCode = null,
    bool? IsActive = null) : IQuery<PagedResult<BranchListItem>>, IRequirePermission
{
    public const string DefaultSort = "name";

    public static readonly SortMap<Branch> Sorts = SortMap<Branch>.By(b => b.Id)
        .Add("code", b => b.Code)
        .Add("name", b => b.Name)
        .Add("type", b => b.Type)
        .Add("isActive", b => b.IsActive)
        .Add("createdAt", b => b.CreatedAt);

    public string Permission => PlatformPermissions.Branches.Read;
}

/// <remarks>City and District are the province and district names.</remarks>
public sealed record BranchListItem(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    BranchType Type,
    string City,
    string? District,
    bool IsActive);

/// <summary>One branch in the caller's scope; otherwise 404.</summary>
public sealed record GetBranchQuery(Guid Id) : IQuery<BranchDetails>, IRequirePermission
{
    public string Permission => PlatformPermissions.Branches.Read;
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record BranchDetails(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    BranchType Type,
    AddressDto Address,
    string? SgkWorkplaceNumber,
    string? Phone,
    bool IsActive,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetBranchesQueryValidator : AbstractValidator<GetBranchesQuery>
{
    public GetBranchesQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetBranchesQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleFor(q => q.Type).IsInEnum();
    }
}

internal sealed class GetBranchesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ICompanyContext companyContext)
    : IQueryHandler<GetBranchesQuery, PagedResult<BranchListItem>>
{
    public async Task<Result<PagedResult<BranchListItem>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var branches = scope.FilterBranches(db.Branches.AsNoTracking());

        foreach (var companyFilter in new[] { request.CompanyId, companyContext.ActiveCompanyId }.OfType<Guid>().Distinct())
        {
            var companyId = CompanyId.From(companyFilter);
            branches = branches.Where(b => b.CompanyId == companyId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            branches = branches.Where(b => TextSearch.Matches(b.Code, pattern) || TextSearch.Matches(b.Name, pattern));
        }

        if (request.Type is { } type)
        {
            branches = branches.Where(b => b.Type == type);
        }

        if (request.ProvinceCode is { } provinceCode)
        {
            branches = branches.Where(b => b.Address.ProvinceCode == provinceCode);
        }

        if (request.IsActive is { } isActive)
        {
            branches = branches.Where(b => b.IsActive == isActive);
        }

        var companies = db.Companies.AsNoTracking();
        return await GetBranchesQuery.Sorts.Apply(branches, request.Paging.Sort, GetBranchesQuery.DefaultSort)
            .Select(b => new BranchListItem(
                b.Id.Value,
                b.CompanyId.Value,
                companies.Where(c => c.Id == b.CompanyId).Select(c => c.ShortName).First(),
                b.Code,
                b.Name,
                b.Type,
                b.Address.City,
                b.Address.District,
                b.IsActive))
            .ToPagedResultAsync(request.Paging, cancellationToken);
    }
}

internal sealed class GetBranchQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes) : IQueryHandler<GetBranchQuery, BranchDetails>
{
    public async Task<Result<BranchDetails>> Handle(GetBranchQuery request, CancellationToken cancellationToken)
    {
        var id = BranchId.From(request.Id);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var row = await scope.FilterBranches(db.Branches.AsNoTracking())
            .Where(b => b.Id == id)
            .Select(b => new
            {
                Branch = b,
                CompanyName = db.Companies.Where(c => c.Id == b.CompanyId).Select(c => c.ShortName).First(),
                Version = EF.Property<uint>(b, ConcurrencyVersion.PropertyName),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var branch = row.Branch;
        return new BranchDetails(
            branch.Id.Value,
            branch.CompanyId.Value,
            row.CompanyName,
            branch.Code,
            branch.Name,
            branch.Type,
            AddressDto.From(branch.Address)!,
            branch.SgkWorkplaceNumber,
            branch.Phone,
            branch.IsActive,
            row.Version,
            branch.CreatedAt,
            branch.ModifiedAt);
    }
}
