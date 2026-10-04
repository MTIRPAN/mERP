namespace Merp.Application.Abstractions.Context;

/// <summary>The caller of the current request or job.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>True for background jobs and system processes; they bypass permission checks.</summary>
    bool IsSystem { get; }

    /// <summary>Platform user id (not the Keycloak subject). Null for anonymous requests.</summary>
    Guid? UserId { get; }

    /// <summary>HR employee linked to the user (D6), if any.</summary>
    Guid? EmployeeId { get; }

    Guid? TenantId { get; }

    /// <summary>Culture used for messages, e.g. <c>tr-TR</c>.</summary>
    string Culture { get; }
}
