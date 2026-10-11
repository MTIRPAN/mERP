using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Domain.Approvals;

public readonly record struct DelegationId(Guid Value) : IEntityId<DelegationId>
{
    public static DelegationId From(Guid value) => new(value);
}

/// <summary>
/// A user's approvals handed to a delegate for a period (§2.1, §2.6): the delegate sees the principal's tasks and
/// decides "on behalf of" them. Empty document types cover every type. Both dates are inclusive.
/// </summary>
[Audited]
public sealed class Delegation : AuditableAggregateRoot<DelegationId>
{
    public static readonly Error SelfDelegation = Error.Validation([new FieldError("delegateUserId", "platform.approvals.errors.self_delegation")]);

    private Delegation()
    {
    }

    public Guid PrincipalUserId { get; private set; }

    public Guid DelegateUserId { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public IReadOnlyList<string> DocumentTypes { get; private set; } = [];

    /// <summary>Ended early by the principal; a revoked delegation covers nothing.</summary>
    public bool IsRevoked { get; private set; }

    public static Result<Delegation> Create(Guid principal, Guid @delegate, DateOnly from, DateOnly to, IReadOnlyList<string> documentTypes)
    {
        if (principal == @delegate)
        {
            return SelfDelegation;
        }

        if (to < from)
        {
            return CommonErrors.InvalidDateRange.ForField("to");
        }

        return new Delegation
        {
            Id = EntityId.New<DelegationId>(),
            PrincipalUserId = principal,
            DelegateUserId = @delegate,
            From = from,
            To = to,
            DocumentTypes = [.. documentTypes.Distinct(StringComparer.Ordinal)],
        };
    }

    public void Revoke() => IsRevoked = true;

    public bool Covers(string documentType, DateOnly day) =>
        !IsRevoked && From <= day && day <= To && (DocumentTypes.Count == 0 || DocumentTypes.Contains(documentType, StringComparer.Ordinal));
}
