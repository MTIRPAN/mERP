using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Organization;

// Integration events of companies and branches (PLAN P1). IntegrationEvent.CompanyId is always set: it is the
// company itself, or the company of the branch.

/// <param name="Code">Company code; it never changes.</param>
/// <param name="ShortName">Name shown in lists.</param>
/// <param name="TimeZoneId">IANA time zone of the company's day boundaries.</param>
/// <param name="IsActive">Inactive companies are hidden from selectors.</param>
public sealed record CompanyCreated(string Code, string ShortName, string TimeZoneId, bool IsActive) : IntegrationEvent;

/// <summary>Raised whenever a company's details or its active flag change.</summary>
public sealed record CompanyUpdated(string Code, string ShortName, string TimeZoneId, bool IsActive) : IntegrationEvent;

/// <param name="BranchId">The branch.</param>
/// <param name="Code">Branch code, unique within the company; it never changes.</param>
/// <param name="Name">Branch name.</param>
/// <param name="Type">HeadOffice, Factory, Warehouse, Store or Office.</param>
/// <param name="ProvinceCode">Province plate number of a Turkish branch.</param>
/// <param name="IsActive">Inactive branches are hidden from selectors.</param>
public sealed record BranchCreated(Guid BranchId, string Code, string Name, string Type, int? ProvinceCode, bool IsActive) : IntegrationEvent;

/// <summary>Raised whenever a branch's details or its active flag change.</summary>
public sealed record BranchUpdated(Guid BranchId, string Code, string Name, string Type, int? ProvinceCode, bool IsActive) : IntegrationEvent;
