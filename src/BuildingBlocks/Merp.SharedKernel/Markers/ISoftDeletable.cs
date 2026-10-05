namespace Merp.SharedKernel.Markers;

/// <summary>
/// Master data that is hidden instead of deleted (D9). Deleting the entity sets the flag through the EF
/// interceptor and a global query filter hides it. Documents are cancelled, never deleted.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }
}
