using System.Linq.Expressions;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Companies;

public sealed record ListCompaniesQuery(
    int Page = 1,
    int PageSize = PagingLimits.DefaultPageSize,
    string? Sort = null,
    string? Filter = null,
    bool? IsActive = null) : IQuery<PagedResult<CompanyListItem>>, IPagedRequest, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Read;
}

public sealed record CompanyListItem(Guid Id, string Code, string Title, string ShortName, string TaxNumber, string City, string? District, bool IsActive);

public sealed record GetCompanyQuery(Guid Id) : IQuery<CompanyDto>, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Read;
}

/// <param name="Id">Company id.</param>
/// <param name="Version">Concurrency token; send it back unchanged with an update.</param>
/// <param name="Code">Unique code; cannot change.</param>
/// <param name="Title">Registered title (ticaret unvanı).</param>
/// <param name="ShortName">Name shown in lists and the company selector.</param>
/// <param name="TaxOffice">Tax office (vergi dairesi).</param>
/// <param name="TaxNumber">Tax number (VKN), 10 digits.</param>
/// <param name="MersisNo">MERSİS number, 16 digits.</param>
/// <param name="SgkWorkplaceNo">SGK workplace registration number of the head office, 26 digits.</param>
/// <param name="AddressLine1">Street address.</param>
/// <param name="AddressLine2">Additional address line.</param>
/// <param name="ProvinceCode">Province (il) code, 1–81.</param>
/// <param name="ProvinceName">Province name.</param>
/// <param name="DistrictId">District (ilçe) id.</param>
/// <param name="DistrictName">District name.</param>
/// <param name="PostalCode">Five digit postal code.</param>
/// <param name="Phone">Phone in E.164 form.</param>
/// <param name="Email">E-mail address.</param>
/// <param name="DefaultCulture">Culture for documents and e-mails, e.g. tr-TR.</param>
/// <param name="TimeZoneId">IANA time zone, e.g. Europe/Istanbul.</param>
/// <param name="IsActive">Inactive companies are hidden from selectors.</param>
public sealed record CompanyDto(
    Guid Id,
    uint Version,
    string Code,
    string Title,
    string ShortName,
    string? TaxOffice,
    string TaxNumber,
    string? MersisNo,
    string? SgkWorkplaceNo,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    string ProvinceName,
    int DistrictId,
    string? DistrictName,
    string? PostalCode,
    string? Phone,
    string? Email,
    string DefaultCulture,
    string TimeZoneId,
    bool IsActive);

/// <summary>Active companies the caller may work with, for the company selector (D4).</summary>
public sealed record GetAccessibleCompaniesQuery(IReadOnlyCollection<string> GrantedPermissions) : IQuery<IReadOnlyList<CompanyOption>>;

public sealed record CompanyOption(Guid Id, string Code, string Name);

internal static class CompanyProjections
{
    public static readonly SortMap<Company> Sorts = new SortMap<Company>()
        .Add("code", c => c.Code)
        .Add("title", c => c.Title)
        .Add("shortName", c => c.ShortName)
        .Add("city", c => c.Address.City)
        .Add("isActive", c => c.IsActive)
        .Add("id", c => c.Id);

    public static readonly Expression<Func<Company, CompanyListItem>> ListItem = c => new CompanyListItem(
        c.Id.Value, c.Code, c.Title, c.ShortName, c.TaxNumber.Value, c.Address.City, c.Address.District, c.IsActive);

    public static CompanyDto ToDto(Company c, uint version) => new(
        c.Id.Value,
        version,
        c.Code,
        c.Title,
        c.ShortName,
        c.TaxOffice,
        c.TaxNumber.Value,
        c.MersisNo?.Value,
        c.SgkWorkplaceNo?.Value,
        c.Address.Line1,
        c.Address.Line2,
        c.ProvinceCode,
        c.Address.City,
        c.DistrictId,
        c.Address.District,
        c.Address.PostalCode,
        c.Phone?.Value,
        c.Email?.Value,
        c.DefaultCulture,
        c.TimeZoneId,
        c.IsActive);
}

internal sealed class ListCompaniesQueryValidator : AbstractValidator<ListCompaniesQuery>
{
    public ListCompaniesQueryValidator() => this.ValidatePaging(CompanyProjections.Sorts.Names);
}

internal sealed class ListCompaniesQueryHandler(IPlatformDbContext db, PlatformAccess access)
    : IQueryHandler<ListCompaniesQuery, PagedResult<CompanyListItem>>
{
    public async Task<Result<PagedResult<CompanyListItem>>> Handle(ListCompaniesQuery request, CancellationToken cancellationToken)
    {
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var query = scope.Companies(db, includeParentsOfBranches: true).AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            var term = TurkishText.Fold(request.Filter.Trim());
            query = query.Where(c => EF.Property<string>(c, ShadowProperties.SearchText).Contains(term));
        }

        return await query.ToPagedResultAsync(request, CompanyProjections.Sorts, "shortName", "id", CompanyProjections.ListItem, cancellationToken);
    }
}

internal sealed class GetCompanyQueryHandler(IPlatformDbContext db, PlatformAccess access) : IQueryHandler<GetCompanyQuery, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(GetCompanyQuery request, CancellationToken cancellationToken)
    {
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var id = CompanyId.From(request.Id);
        var company = await scope.Companies(db, includeParentsOfBranches: true)
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { Company = c, Version = EF.Property<uint>(c, ShadowProperties.Version) })
            .FirstOrDefaultAsync(cancellationToken);

        // Out of scope looks the same as missing (docs/domain/yetki-ve-onay.md §1.3).
        return company is null ? CommonErrors.NotFound : CompanyProjections.ToDto(company.Company, company.Version);
    }
}

internal sealed class GetAccessibleCompaniesQueryHandler(IPlatformDbContext db, PlatformAccess access)
    : IQueryHandler<GetAccessibleCompaniesQuery, IReadOnlyList<CompanyOption>>
{
    public async Task<Result<IReadOnlyList<CompanyOption>>> Handle(GetAccessibleCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (request.GrantedPermissions.Count == 0)
        {
            return Result.Success<IReadOnlyList<CompanyOption>>([]);
        }

        var scope = await access.GetAnyAsync(request.GrantedPermissions, cancellationToken);
        var companies = await scope.Companies(db, includeParentsOfBranches: true)
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.ShortName)
            .Select(c => new CompanyOption(c.Id.Value, c.Code, c.ShortName))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<CompanyOption>>(companies);
    }
}
