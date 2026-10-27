using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.Contracts.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Organization;

/// <summary><see cref="ICompanyLogos"/>: the latest logo file of each company.</summary>
public sealed class CompanyLogos(IPlatformDbContext db, IFileStore files) : ICompanyLogos
{
    public async Task<IReadOnlySet<Guid>> ListWithLogoAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(companyIds);
        var ids = companyIds.Distinct().ToList();
        return (await Logos().Where(f => ids.Contains(f.OwnerId)).Select(f => f.OwnerId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }

    public async Task<CompanyLogo?> FindAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var logo = await Logos().Where(f => f.OwnerId == companyId).OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return logo is null ? null : new CompanyLogo(await files.OpenReadAsync(logo, cancellationToken), logo.ContentType);
    }

    private IQueryable<StoredFile> Logos() =>
        db.StoredFiles.AsNoTracking().Where(f => f.OwnerType == PlatformFileOwners.Company && f.Purpose == StoredFilePurposes.Logo);
}
