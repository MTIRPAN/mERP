namespace Merp.ArchitectureTests.Samples.BadContracts;

/// <summary>Deliberate violation: a contracts interface that writes and is synchronous (rule 3). See RuleSelfTests.</summary>
public interface IEmployeeCommands
{
    Task CreateEmployeeAsync(string name);

    string GetName(Guid id);

    Task<string?> FindNameAsync(Guid id);
}
