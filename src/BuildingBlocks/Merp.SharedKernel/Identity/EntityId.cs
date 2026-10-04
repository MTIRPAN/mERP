namespace Merp.SharedKernel.Identity;

public static class EntityId
{
    /// <summary>Creates a new time-ordered identifier.</summary>
    public static TId New<TId>()
        where TId : struct, IEntityId<TId> =>
        TId.From(Guid.CreateVersion7());

    /// <summary>Parses an identifier, returning <c>false</c> for malformed or empty values.</summary>
    public static bool TryParse<TId>(string? value, out TId id)
        where TId : struct, IEntityId<TId>
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = TId.From(guid);
            return true;
        }

        id = default;
        return false;
    }
}
