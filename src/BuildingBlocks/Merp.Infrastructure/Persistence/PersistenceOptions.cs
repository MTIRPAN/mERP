namespace Merp.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>
    /// Fails a command that modifies more than one aggregate root (CLAUDE.md rule 7, D21). Enabled by default;
    /// the API turns it off outside Development and Testing.
    /// </summary>
    public bool EnforceSingleAggregatePerCommand { get; set; } = true;

    /// <summary>Upper bound for domain event rounds in one commit; protects against handlers that raise events forever.</summary>
    public int MaxDomainEventRounds { get; set; } = 10;
}
