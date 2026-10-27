namespace Merp.Modules.Platform.Contracts.Organization;

/// <summary>A company logo to send: PNG, JPEG or WebP.</summary>
/// <param name="Content">The image; the caller disposes it.</param>
/// <param name="ContentType">Its media type.</param>
public sealed record CompanyLogo(Stream Content, string ContentType);

/// <summary>Company logos for other modules, e.g. the career page (D49).</summary>
public interface ICompanyLogos
{
    /// <summary>The companies among <paramref name="companyIds"/> that have a logo.</summary>
    Task<IReadOnlySet<Guid>> ListWithLogoAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken cancellationToken = default);

    /// <summary>The company's logo, opened for reading; null when it has none.</summary>
    Task<CompanyLogo?> FindAsync(Guid companyId, CancellationToken cancellationToken = default);
}
