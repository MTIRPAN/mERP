namespace Merp.Modules.Platform.Contracts.Access;

/// <summary>
/// Who holds a permission, for other modules that notify the people responsible for a record (D38, e.g. HR's
/// document reminders). Read only.
/// </summary>
public interface IPermissionHolders
{
    /// <summary>
    /// Active users with a role assignment valid today that grants <paramref name="permission"/> with the Tenant scope
    /// or the Company scope of <paramref name="companyId"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsAsync(string permission, Guid companyId, CancellationToken cancellationToken = default);
}
