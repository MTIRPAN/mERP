namespace Merp.SharedKernel.Domain;

/// <summary>
/// Something that happened inside an aggregate. Domain events are handled in the same module and in the same
/// transaction (CLAUDE.md rule 7); events for other modules are integration events published through the outbox.
/// </summary>
public interface IDomainEvent;
