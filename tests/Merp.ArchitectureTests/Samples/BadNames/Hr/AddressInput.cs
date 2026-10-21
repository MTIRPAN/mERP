namespace Merp.ArchitectureTests.Samples.BadNames.Hr;

/// <summary>Deliberate violation: the same name as a type of another module. See RuleSelfTests.</summary>
public sealed record AddressInput(string Line);

/// <summary>Interfaces are never schemas; a shared name is allowed.</summary>
public interface IAddressFields;
