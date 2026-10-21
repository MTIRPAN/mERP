namespace Merp.ArchitectureTests.Samples.BadNames.Platform;

/// <summary>Deliberate violation: the same name as a type of another module. See RuleSelfTests.</summary>
public sealed record AddressInput(string Line1, string? Line2);

/// <summary>Interfaces are never schemas; a shared name is allowed.</summary>
public interface IAddressFields;
