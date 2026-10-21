namespace Merp.Modules.Platform.Contracts.Users;

/// <param name="Id">Platform user.</param>
/// <param name="DisplayName">Name shown in lists.</param>
/// <param name="UserName">Sign-in name.</param>
/// <param name="Email">E-mail address, if any.</param>
/// <param name="IsActive">Inactive users cannot sign in.</param>
/// <param name="EmployeeId">The HR employee the user is linked to, if any.</param>
public sealed record UserSummary(Guid Id, string DisplayName, string UserName, string? Email, bool IsActive, Guid? EmployeeId);

/// <summary>
/// Users for other modules (D39): HR picks the user to link to an employee and shows whether the linked user is
/// active. Read only.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Active users whose name, user name or e-mail contains <paramref name="term"/>, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<UserSummary>> SearchAsync(string? term, int limit, CancellationToken cancellationToken = default);

    /// <summary>The users among <paramref name="ids"/> that exist.</summary>
    Task<IReadOnlyList<UserSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
