namespace Merp.SharedKernel.Identity;

/// <summary>
/// Strongly typed identifier backed by a UUID v7 (D9). Implement as
/// <c>public readonly record struct EmployeeId(Guid Value) : IEntityId&lt;EmployeeId&gt;</c>.
/// </summary>
public interface IEntityId
{
    Guid Value { get; }
}

/// <summary>Enables generic conversions (EF value converters, JSON, route binding).</summary>
public interface IEntityId<TSelf> : IEntityId
    where TSelf : struct, IEntityId<TSelf>
{
    static abstract TSelf From(Guid value);
}
