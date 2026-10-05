using Merp.Application.Abstractions.Context;

namespace Merp.Infrastructure.Security;

/// <summary>
/// Scoped holder of the caller. The web host fills it from the authenticated principal; background jobs mark it as
/// <see cref="SetSystem">system</see>.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string DefaultCulture = "tr-TR";

    public bool IsAuthenticated { get; private set; }

    public bool IsSystem { get; private set; }

    public Guid? UserId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public Guid? TenantId { get; private set; }

    public string Culture { get; private set; } = DefaultCulture;

    public void SetUser(Guid userId, Guid tenantId, Guid? employeeId = null, string? culture = null)
    {
        IsAuthenticated = true;
        IsSystem = false;
        UserId = userId;
        TenantId = tenantId;
        EmployeeId = employeeId;
        Culture = culture ?? Culture;
    }

    /// <summary>Marks the scope as a system process (background job, migration, outbox dispatch).</summary>
    public void SetSystem(Guid? tenantId = null, Guid? actingUserId = null)
    {
        IsAuthenticated = true;
        IsSystem = true;
        TenantId = tenantId;
        UserId = actingUserId;
        EmployeeId = null;
    }

    public void SetCulture(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        Culture = culture;
    }
}
