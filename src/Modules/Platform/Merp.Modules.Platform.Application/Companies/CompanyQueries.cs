using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Companies;

/// <summary>Companies in the caller's scope, filtered and paged.</summary>
/// <remarks>Search matches part of the code, short name, title or tax number.</remarks>
public sealed record GetCompaniesQuery(PageRequest Paging, string? Search = null, bool? IsActive = null)
    : IQuery<PagedResult<CompanyListItem>>, IRequirePermission
{
    public const string DefaultSort = "shortName";

    public static readonly SortMap<Company> Sorts = SortMap<Company>.By(c => c.Id)
        .Add("code", c => c.Code)
        .Add("shortName", c => c.ShortName)
        .Add("title", c => c.Title)
        .Add("taxNumber", c => c.TaxNumber)
        .Add("isActive", c => c.IsActive)
        .Add("createdAt", c => c.CreatedAt);

    public string Permission => PlatformPermissions.Companies.Read;
}

public sealed record CompanyListItem(Guid Id, string Code, string ShortName, string Title, string TaxNumber, string? City, bool IsActive);

/// <summary>One company in the caller's scope; otherwise 404.</summary>
public sealed record GetCompanyQuery(Guid Id) : IQuery<CompanyDetails>, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Read;
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record CompanyDetails(
    Guid Id,
    string Code,
    string Title,
    string ShortName,
    string? TaxOffice,
    string TaxNumber,
    string? MersisNumber,
    string? SgkWorkplaceNumber,
    AddressDto? Address,
    string? Phone,
    string? Email,
    string Culture,
    string TimeZoneId,
    bool IsActive,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

/// <summary>
/// Active companies the caller can work in, for the company selector (D4): the union of the scopes of the given
/// permissions. Called by <c>/api/platform/me</c> with the caller's granted permissions.
/// </summary>
public sealed record GetSelectableCompaniesQuery(IReadOnlyCollection<string> GrantedPermissions) : IQuery<IReadOnlyList<CompanyOption>>;

public sealed record CompanyOption(Guid Id, string Code, string Name);

internal sealed class GetCompaniesQueryValidator : AbstractValidator<GetCompaniesQuery>
{
    public GetCompaniesQueryValidator() =>
        RuleFor(q => q.Paging.Sort).Must(GetCompaniesQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
}

internal sealed class GetCompaniesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetCompaniesQuery, PagedResult<CompanyListItem>>
{
    public async Task<Result<PagedResult<CompanyListItem>>> Handle(GetCompaniesQuery request, CancellationToken cancellationToken)
    {
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var companies = (await scope.FilterCompaniesAsync(db, cancellationToken)).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            companies = companies.Where(c => TextSearch.Matches(c.Code, pattern)
                || TextSearch.Matches(c.ShortName, pattern)
                || TextSearch.Matches(c.Title, pattern)
                || TextSearch.Matches(c.TaxNumber, pattern));
        }

        if (request.IsActive is { } isActive)
        {
            companies = companies.Where(c => c.IsActive == isActive);
        }

        return await GetCompaniesQuery.Sorts.Apply(companies, request.Paging.Sort, GetCompaniesQuery.DefaultSort)
            .Select(c => new CompanyListItem(c.Id.Value, c.Code, c.ShortName, c.Title, c.TaxNumber, c.Address == null ? null : c.Address.City, c.IsActive))
            .ToPagedResultAsync(request.Paging, cancellationToken);
    }
}

internal sealed class GetCompanyQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes) : IQueryHandler<GetCompanyQuery, CompanyDetails>
{
    public async Task<Result<CompanyDetails>> Handle(GetCompanyQuery request, CancellationToken cancellationToken)
    {
        var id = CompanyId.From(request.Id);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var company = await (await scope.FilterCompaniesAsync(db, cancellationToken))
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { Company = c, Version = EF.Property<uint>(c, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return CommonErrors.NotFound;
        }

        var c = company.Company;
        return new CompanyDetails(
            c.Id.Value,
            c.Code,
            c.Title,
            c.ShortName,
            c.TaxOffice,
            c.TaxNumber,
            c.MersisNumber,
            c.SgkWorkplaceNumber,
            AddressDto.From(c.Address),
            c.Phone,
            c.Email,
            c.Culture,
            c.TimeZoneId,
            c.IsActive,
            company.Version,
            c.CreatedAt,
            c.ModifiedAt);
    }
}

internal sealed class GetSelectableCompaniesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetSelectableCompaniesQuery, IReadOnlyList<CompanyOption>>
{
    public async Task<Result<IReadOnlyList<CompanyOption>>> Handle(GetSelectableCompaniesQuery request, CancellationToken cancellationToken)
    {
        var granted = new List<AccessScope>();
        foreach (var permission in request.GrantedPermissions)
        {
            granted.AddRange(await scopes.GetCurrentUserScopesAsync(permission, cancellationToken));
        }

        var scope = OrganizationScope.From(granted);
        if (scope.IsEmpty)
        {
            return Array.Empty<CompanyOption>();
        }

        return await (await scope.FilterCompaniesAsync(db, cancellationToken))
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.ShortName)
            .ThenBy(c => c.Code)
            .Select(c => new CompanyOption(c.Id.Value, c.Code, c.ShortName))
            .ToListAsync(cancellationToken);
    }
}
