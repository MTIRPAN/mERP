using Merp.Modules.Platform.Domain.Roles;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Domain.Users;

public readonly record struct UserId(Guid Value) : IEntityId<UserId>
{
    public static UserId From(Guid value) => new(value);
}

public readonly record struct RoleAssignmentId(Guid Value) : IEntityId<RoleAssignmentId>
{
    public static RoleAssignmentId From(Guid value) => new(value);
}

/// <summary>
/// A person who signs in (D6): the tenant's mirror of a Keycloak user, matched by the token's <c>sub</c>. Holds the
/// role assignments that give the user permissions in data scopes. Not every user is an employee.
/// </summary>
[Audited]
public sealed class User : AuditableAggregateRoot<UserId>
{
    public const int SubjectMaxLength = 64;
    /// <summary>Keycloak refuses shorter user names (its username length validator).</summary>
    public const int UserNameMinLength = 3;

    public const int UserNameMaxLength = 255;
    public const int NameMaxLength = 100;

    private readonly List<RoleAssignment> _assignments = [];

    private User()
    {
    }

    /// <summary>Keycloak user id (<c>sub</c>), unique in the tenant.</summary>
    public string Subject { get; private set; } = null!;

    public string UserName { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>HR employee of the user (D6), linked by HR (PLAN H3).</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>Preferred culture; null follows the browser (D11).</summary>
    public string? Culture { get; private set; }

    /// <summary>The user's choices that differ from the notification types' defaults (PLAN P8).</summary>
    public IReadOnlyList<NotificationPreference> NotificationPreferences { get; private set; } = [];

    public IReadOnlyCollection<RoleAssignment> Assignments => _assignments.AsReadOnly();

    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : UserName;

    public static User Create(string subject, UserProfile profile)
    {
        var user = new User
        {
            Id = EntityId.New<UserId>(),
            Subject = Text.Required(subject, SubjectMaxLength, nameof(subject)),
            IsActive = true,
        };
        user.UpdateProfile(profile);
        return user;
    }

    /// <summary>Copies the identity provider's profile; returns true when something changed.</summary>
    public bool UpdateProfile(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var userName = Text.Required(profile.UserName, UserNameMaxLength, nameof(profile.UserName));
        var email = Text.Optional(profile.Email?.ToLowerInvariant(), UserNameMaxLength, nameof(profile.Email));
        var firstName = Text.Optional(profile.FirstName, NameMaxLength, nameof(profile.FirstName));
        var lastName = Text.Optional(profile.LastName, NameMaxLength, nameof(profile.LastName));
        var changed = !string.Equals(UserName, userName, StringComparison.Ordinal)
            || !string.Equals(Email, email, StringComparison.Ordinal)
            || !string.Equals(FirstName, firstName, StringComparison.Ordinal)
            || !string.Equals(LastName, lastName, StringComparison.Ordinal);

        (UserName, Email, FirstName, LastName) = (userName, email, firstName, lastName);
        return changed;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Links the user to an HR employee (D6, D39); the link is unique in the tenant both ways.</summary>
    public void LinkEmployee(Guid employeeId) =>
        EmployeeId = employeeId == Guid.Empty ? throw new ArgumentException("An employee id is required.", nameof(employeeId)) : employeeId;

    public void UnlinkEmployee() => EmployeeId = null;

    /// <summary>Whether the user holds the role with the scope at all, whatever the validity.</summary>
    public bool Holds(RoleId roleId, AssignmentScope scope) => _assignments.Any(a => a.RoleId == roleId && a.Scope == scope);

    public void SetCulture(string? culture) => Culture = string.IsNullOrWhiteSpace(culture) ? null : culture.Trim();

    /// <summary>Whether the user gets a notification type on a channel: their choice, otherwise the type's default.</summary>
    public bool Receives(string notificationType, NotificationChannel channel, bool byDefault) =>
        NotificationPreferences.FirstOrDefault(p => p.Type == notificationType && p.Channel == channel)?.Enabled ?? byDefault;

    /// <summary>Records the user's choice; a choice equal to the default is not kept, so later default changes apply.</summary>
    public void SetNotificationPreference(string notificationType, NotificationChannel channel, bool enabled, bool byDefault)
    {
        var type = Text.Required(notificationType, 100, nameof(notificationType));
        var others = NotificationPreferences.Where(p => !(p.Type == type && p.Channel == channel));
        var choices = enabled == byDefault ? others : others.Append(new NotificationPreference(type, channel, enabled));
        NotificationPreferences = [.. choices.OrderBy(p => p.Type, StringComparer.Ordinal).ThenBy(p => p.Channel)];
    }

    public Result<RoleAssignment> Assign(RoleId roleId, AssignmentScope scope, DateOnly? validFrom, DateOnly? validTo)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (validFrom is not null && validTo is not null && validTo < validFrom)
        {
            return CommonErrors.InvalidDateRange.ForField("validTo");
        }

        if (_assignments.Any(a => a.RoleId == roleId && a.Scope == scope && a.ValidFrom == validFrom && a.ValidTo == validTo))
        {
            return UserErrors.DuplicateAssignment;
        }

        var assignment = new RoleAssignment(Id, roleId, scope, validFrom, validTo);
        _assignments.Add(assignment);
        return assignment;
    }

    public Result ChangeValidity(RoleAssignmentId assignmentId, DateOnly? validFrom, DateOnly? validTo)
    {
        var assignment = _assignments.SingleOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
        {
            return CommonErrors.NotFound;
        }

        if (validFrom is not null && validTo is not null && validTo < validFrom)
        {
            return CommonErrors.InvalidDateRange.ForField("validTo");
        }

        assignment.ChangeValidity(validFrom, validTo);
        return Result.Success();
    }

    public Result Unassign(RoleAssignmentId assignmentId)
    {
        var assignment = _assignments.SingleOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
        {
            return CommonErrors.NotFound;
        }

        _assignments.Remove(assignment);
        return Result.Success();
    }

    /// <summary>Assignments in force on <paramref name="day"/>; an inactive user has none.</summary>
    public IEnumerable<RoleAssignment> ValidAssignments(DateOnly day) =>
        IsActive ? _assignments.Where(a => a.IsValidOn(day)) : [];
}

/// <summary>Profile data mirrored from the identity provider.</summary>
public sealed record UserProfile(string UserName, string? Email, string? FirstName, string? LastName);

/// <summary>
/// Where an assignment applies (docs/domain/yetki-ve-onay.md §1.2): a scope type code registered by a module and,
/// for types that name a record, its id. Platform stores it opaquely.
/// </summary>
public sealed record AssignmentScope
{
    public const int TypeMaxLength = 50;

    public AssignmentScope(string type, Guid? scopeId = null, bool includeSubUnits = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type.Length <= TypeMaxLength ? type : throw new ArgumentException($"A scope type has at most {TypeMaxLength} characters.", nameof(type));
        ScopeId = scopeId;
        IncludeSubUnits = includeSubUnits;
    }

    public string Type { get; }

    public Guid? ScopeId { get; }

    public bool IncludeSubUnits { get; }
}

/// <summary>A role given to a user in a scope, optionally for a period (both ends inclusive).</summary>
[Audited]
public sealed class RoleAssignment : Entity<RoleAssignmentId>
{
    private RoleAssignment()
    {
    }

    internal RoleAssignment(UserId userId, RoleId roleId, AssignmentScope scope, DateOnly? validFrom, DateOnly? validTo)
        : base(EntityId.New<RoleAssignmentId>())
    {
        UserId = userId;
        RoleId = roleId;
        ScopeType = scope.Type;
        ScopeId = scope.ScopeId;
        IncludeSubUnits = scope.IncludeSubUnits;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public UserId UserId { get; private set; }

    public RoleId RoleId { get; private set; }

    public string ScopeType { get; private set; } = null!;

    public Guid? ScopeId { get; private set; }

    public bool IncludeSubUnits { get; private set; }

    public DateOnly? ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public AssignmentScope Scope => new(ScopeType, ScopeId, IncludeSubUnits);

    public bool IsValidOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);

    internal void ChangeValidity(DateOnly? validFrom, DateOnly? validTo) => (ValidFrom, ValidTo) = (validFrom, validTo);
}

/// <summary>Domain errors of users; texts in the Platform module's <c>tr-TR.json</c>.</summary>
public static class UserErrors
{
    public static readonly Error DuplicateAssignment = Error.Conflict("platform.users.errors.duplicate_assignment");
}

/// <summary>A user's choice for a notification type on a channel.</summary>
public sealed record NotificationPreference(string Type, NotificationChannel Channel, bool Enabled);
