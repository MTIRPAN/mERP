using Merp.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Merp.Infrastructure.Persistence.Conventions;

/// <summary>Stores a strongly typed id as a uuid column.</summary>
public sealed class StronglyTypedIdConverter<TId>() : ValueConverter<TId, Guid>(id => id.Value, value => FromGuid(value))
    where TId : struct, IEntityId<TId>
{
    // Expression trees cannot call static abstract interface members directly.
    private static TId FromGuid(Guid value) => TId.From(value);
}
