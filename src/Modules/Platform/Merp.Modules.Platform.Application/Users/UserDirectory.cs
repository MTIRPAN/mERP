using Merp.Application.Persistence;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Users;

/// <inheritdoc cref="IUserDirectory"/>
public sealed class UserDirectory(IPlatformDbContext db) : IUserDirectory
{
    private const int MaxResults = 50;

    public async Task<IReadOnlyList<UserSummary>> SearchAsync(string? term, int limit, CancellationToken cancellationToken = default)
    {
        var users = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = TextSearch.ContainsPattern(term);
            users = users.Where(u =>
                TextSearch.Matches(u.UserName, pattern)
                || TextSearch.Matches(u.Email, pattern)
                || TextSearch.Matches(u.FirstName, pattern)
                || TextSearch.Matches(u.LastName, pattern)
                || TextSearch.Matches(u.FirstName + " " + u.LastName, pattern));
        }

        var rows = await users
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ThenBy(u => u.UserName)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .Select(u => new { u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.IsActive, u.EmployeeId })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(u => Summary(u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.IsActive, u.EmployeeId))];
    }

    public async Task<IReadOnlyList<UserSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var userIds = ids.Distinct().Select(UserId.From).ToList();
        var rows = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.IsActive, u.EmployeeId })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(u => Summary(u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.IsActive, u.EmployeeId))];
    }

    private static UserSummary Summary(UserId id, string userName, string? email, string? firstName, string? lastName, bool isActive, Guid? employeeId)
    {
        var name = $"{firstName} {lastName}".Trim();
        return new UserSummary(id.Value, name.Length > 0 ? name : userName, userName, email, isActive, employeeId);
    }
}
