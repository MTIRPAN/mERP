using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Branches;

/// <param name="Page">Page number, from 1.</param>
/// <param name="PageSize">Rows per page, at most 200.</param>
/// <param name="Sort">code, name, type, city, companyCode or isActive; a leading "-" sorts descending.</param>
/// <param name="Filter">Searches code, name, city and district, ignoring case and Turkish letters.</param>
/// <param name="CompanyId">Only branches of this company. The active company header narrows the list as well.</param>
/// <param name="IsActive">Only active or only inactive branches.</param>
public sealed record ListBranchesQuery(
    int Page = 1,
    int PageSize = PagingLimits.DefaultPageSize,
    string? Sort = null,
    string? Filter = null,
    Guid? CompanyId = null,
    bool? IsActive = null) : IQuery<PagedResult<BranchListItem>>, IPagedRequest, IRequirePermission
{
    public string Permission => PlatformPermissions.Branches.Read;
}

public sealed record BranchListItem(
    Guid Id,
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    string Code,
    string Name,
    BranchType Type,
    string City,
    string? District,
    bool IsActive);

public sealed record GetBranchQuery(Guid Id) : IQuery<BranchDto>, IRequirePermission
{
    public string Permission => PlatformPermissions.Branches.Read;
}

/// <param name="Id">Branch id.</param>
/// <param name="Version">Concurrency token; send it back unchanged with an update.</param>
/// <param name="CompanyId">Owning company.</param>
/// <param name="CompanyName">Short name of the company.</param>
/// <param name="Code">Unique within the company; cannot change.</param>
/// <param name="Name">Branch name.</param>
/// <param name="Type">Headquarters, Factory, Warehouse, Store or Office.</param>
/// <param name="AddressLine1">Street address.</param>
/// <param name="AddressLine2">Additional address line.</param>
/// <param name="ProvinceCode">Province (il) code.</param>
/// <param name="ProvinceName">Province name.</param>
/// <param name="DistrictId">District (ilçe) id.</param>
/// <param name="DistrictName">District name.</param>
/// <param name="PostalCode">Five digit postal code.</param>
/// <param name="SgkWorkplaceNo">SGK workplace registration number, 26 digits.</param>
/// <param name="IsActive">Inactive branches are hidden from selectors.</param>
public sealed record BranchDto(
    Guid Id,
    uint Version,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    BranchType Type,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    string ProvinceName,
    int DistrictId,
    string? DistrictName,
    string? PostalCode,
    string? SgkWorkplaceNo,
    bool IsActive);

/// <summary>Branch joined with its company. Built with an object initializer so EF Core can follow its members.</summary>
internal sealed class BranchRow
{
    public required Branch Branch { get; init; }

    public required Company Company { get; init; }
}

internal static class BranchProjections
{
    public static readonly SortMap<BranchRow> Sorts = new SortMap<BranchRow>()
        .Add("code", r => r.Branch.Code)
        .Add("name", r => r.Branch.Name)
        .Add("type", r => r.Branch.Type)
        .Add("city", r => r.Branch.Address.City)
        .Add("companyCode", r => r.Company.Code)
        .Add("isActive", r => r.Branch.IsActive)
        .Add("id", r => r.Branch.Id);

    public static IQueryable<BranchRow> WithCompany(IQueryable<Branch> branches, IPlatformDbContext db) =>
        branches.Join(db.Companies, b => b.CompanyId, c => c.Id, (b, c) => new BranchRow { Branch = b, Company = c });
}

internal sealed class ListBranchesQueryValidator : AbstractValidator<ListBranchesQuery>
{
    public ListBranchesQueryValidator() => this.ValidatePaging(BranchProjections.Sorts.Names);
}

internal sealed class ListBranchesQueryHandler(IPlatformDbContext db, PlatformAccess access, ICompanyContext companyContext)
    : IQueryHandler<ListBranchesQuery, PagedResult<BranchListItem>>
{
    public async Task<Result<PagedResult<BranchListItem>>> Handle(ListBranchesQuery request, CancellationToken cancellationToken)
    {
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var branches = scope.Branches(db).AsNoTracking();

        // Both the filter and the active company (D4) only narrow the scope.
        foreach (var companyGuid in new[] { request.CompanyId, companyContext.ActiveCompanyId }.OfType<Guid>())
        {
            var companyId = CompanyId.From(companyGuid);
            branches = branches.Where(b => b.CompanyId == companyId);
        }

        if (request.IsActive is { } isActive)
        {
            branches = branches.Where(b => b.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            var term = TurkishText.Fold(request.Filter.Trim());
            branches = branches.Where(b => EF.Property<string>(b, ShadowProperties.SearchText).Contains(term));
        }

        return await BranchProjections.WithCompany(branches, db).ToPagedResultAsync(
            request,
            BranchProjections.Sorts,
            "name",
            "id",
            r => new BranchListItem(
                r.Branch.Id.Value,
                r.Branch.CompanyId.Value,
                r.Company.Code,
                r.Company.ShortName,
                r.Branch.Code,
                r.Branch.Name,
                r.Branch.Type,
                r.Branch.Address.City,
                r.Branch.Address.District,
                r.Branch.IsActive),
            cancellationToken);
    }
}

internal sealed class GetBranchQueryHandler(IPlatformDbContext db, PlatformAccess access) : IQueryHandler<GetBranchQuery, BranchDto>
{
    public async Task<Result<BranchDto>> Handle(GetBranchQuery request, CancellationToken cancellationToken)
    {
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var id = BranchId.From(request.Id);
        var row = await scope.Branches(db).AsNoTracking()
            .Where(b => b.Id == id)
            .Join(
                db.Companies,
                b => b.CompanyId,
                c => c.Id,
                (b, c) => new { Branch = b, CompanyName = c.ShortName, Version = EF.Property<uint>(b, ShadowProperties.Version) })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var b = row.Branch;
        return new BranchDto(
            b.Id.Value,
            row.Version,
            b.CompanyId.Value,
            row.CompanyName,
            b.Code,
            b.Name,
            b.Type,
            b.Address.Line1,
            b.Address.Line2,
            b.ProvinceCode,
            b.Address.City,
            b.DistrictId,
            b.Address.District,
            b.Address.PostalCode,
            b.SgkWorkplaceNo?.Value,
            b.IsActive);
    }
}
