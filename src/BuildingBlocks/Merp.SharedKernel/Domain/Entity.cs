namespace Merp.SharedKernel.Domain;

/// <summary>Base type for entities. Equality is identity based.</summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct
{
    protected Entity(TId id) => Id = id;

    /// <summary>Used by EF Core when materializing.</summary>
    protected Entity()
    {
    }

    public TId Id { get; protected init; }

    public bool Equals(Entity<TId>? other) =>
        other is not null
        && (ReferenceEquals(this, other) || (other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id)));

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
