using Merp.SharedKernel.Markers;

namespace Merp.SharedKernel.Domain;

/// <summary>Aggregate root with creation and modification stamps maintained by the persistence layer.</summary>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable
    where TId : struct
{
    protected AuditableAggregateRoot(TId id)
        : base(id)
    {
    }

    protected AuditableAggregateRoot()
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public Guid? ModifiedBy { get; private set; }
}
