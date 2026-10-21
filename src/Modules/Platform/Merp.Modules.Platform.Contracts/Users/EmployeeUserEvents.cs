using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Users;

/// <summary>
/// HR asks for a system user of an employee (D6, D39): Platform opens the identity and the user, sends the invitation,
/// gives the Employee role with the Self scope and links the user to the employee. Answers with
/// <see cref="UserLinkedToEmployee"/> or <see cref="UserProvisioningFailed"/>.
/// </summary>
/// <param name="EmployeeId">The HR employee.</param>
/// <param name="UserName">User name, lower case.</param>
/// <param name="Email">E-mail address the invitation goes to.</param>
/// <param name="FirstName">The person's first name.</param>
/// <param name="LastName">The person's last name.</param>
/// <param name="Culture">Preferred culture, e.g. <c>tr-TR</c>; null follows the browser.</param>
/// <param name="RequestedByUserId">The HR user who asked.</param>
public sealed record UserProvisioningRequested(
    Guid EmployeeId,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    string? Culture,
    Guid? RequestedByUserId) : IntegrationEvent;

/// <summary>
/// HR links an employee to an existing user (D39); the user gets the Employee role with the Self scope unless they
/// have it. Answers with <see cref="UserLinkedToEmployee"/> or <see cref="UserProvisioningFailed"/>.
/// </summary>
public sealed record UserLinkRequested(Guid EmployeeId, Guid UserId, Guid? RequestedByUserId) : IntegrationEvent;

/// <summary>HR removes the link of an employee and a user; the user keeps their roles. Answers with <see cref="UserUnlinkedFromEmployee"/>.</summary>
public sealed record UserUnlinkRequested(Guid EmployeeId, Guid UserId) : IntegrationEvent;

/// <summary>The employee left and no employment remains (D39): Platform deactivates the user and the identity.</summary>
public sealed record UserDeactivationRequested(Guid UserId, Guid EmployeeId) : IntegrationEvent;

/// <summary>Platform linked the user to the employee; HR keeps the user id (D6).</summary>
public sealed record UserLinkedToEmployee(Guid EmployeeId, Guid UserId) : IntegrationEvent;

/// <summary>Platform removed the link of the user and the employee.</summary>
public sealed record UserUnlinkedFromEmployee(Guid EmployeeId, Guid UserId) : IntegrationEvent;

/// <summary>
/// Platform could not create or link the user. ErrorCode is a localization key, e.g.
/// <c>platform.users.errors.user_name_taken</c>.
/// </summary>
public sealed record UserProvisioningFailed(Guid EmployeeId, string ErrorCode) : IntegrationEvent;
