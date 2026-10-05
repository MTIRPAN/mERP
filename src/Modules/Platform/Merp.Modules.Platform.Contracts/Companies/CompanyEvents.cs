using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Companies;

// Integration events of companies and branches (PLAN P1). The company id is in IntegrationEvent.CompanyId.

/// <summary>A company was created.</summary>
public sealed record CompanyCreated(string Code, string Title, string ShortName, bool IsActive) : IntegrationEvent;

/// <summary>A company's data or active state changed. Carries the current values.</summary>
public sealed record CompanyUpdated(string Code, string Title, string ShortName, bool IsActive) : IntegrationEvent;

/// <summary>A branch was created. <c>Type</c> is Headquarters, Factory, Warehouse, Store or Office.</summary>
public sealed record BranchCreated(Guid BranchId, string Code, string Name, string Type, bool IsActive) : IntegrationEvent;

/// <summary>A branch's data or active state changed. Carries the current values.</summary>
public sealed record BranchUpdated(Guid BranchId, string Code, string Name, string Type, bool IsActive) : IntegrationEvent;
