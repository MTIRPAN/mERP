using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Approvals;

// The caller's own delegations (§2.6). Any signed-in user may hand their approvals to another user.

/// <summary>
/// The caller hands their approvals to <paramref name="DelegateUserId"/> from <paramref name="From"/> to
/// <paramref name="To"/>, both inclusive; no document types means all of them.
/// </summary>
public sealed record CreateDelegationCommand(Guid DelegateUserId, DateOnly From, DateOnly To, IReadOnlyList<string>? DocumentTypes) : ICommand<Guid>;

/// <summary>The caller ends one of their delegations.</summary>
public sealed record RevokeDelegationCommand : ICommand
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }
}

internal sealed class CreateDelegationCommandValidator : AbstractValidator<CreateDelegationCommand>
{
    public CreateDelegationCommandValidator()
    {
        RuleFor(c => c.DelegateUserId).NotEmpty();
        RuleFor(c => c.From).NotEmpty();
        RuleFor(c => c.To).NotEmpty();
    }
}

internal sealed class CreateDelegationCommandHandler(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    ApprovalDocumentTypeRegistry documentTypes) : ICommandHandler<CreateDelegationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDelegationCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        var types = (request.DocumentTypes ?? []).Select(t => t.Trim()).ToList();
        var errors = new List<FieldError>();
        if (types.Any(t => documentTypes.Find(t) is null))
        {
            errors.Add(new FieldError("documentTypes", ApprovalErrors.UnknownDocumentTypeCode));
        }

        if (request.DelegateUserId != userId && (await ActiveUsers.FilterAsync(db, [request.DelegateUserId], cancellationToken)).Count == 0)
        {
            errors.Add(new FieldError("delegateUserId", ApprovalErrors.UnknownUserCode));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var delegation = Delegation.Create(userId, request.DelegateUserId, request.From, request.To, types);
        if (delegation.IsFailure)
        {
            return delegation.Error;
        }

        db.Delegations.Add(delegation.Value);
        return delegation.Value.Id.Value;
    }
}

internal sealed class RevokeDelegationCommandHandler(IPlatformDbContext db, ICurrentUser currentUser) : ICommandHandler<RevokeDelegationCommand>
{
    public async Task<Result> Handle(RevokeDelegationCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CommonErrors.Forbidden;
        }

        // Another user's delegation is not found (as their notifications are not, D32)
        var delegation = await db.Delegations.SingleOrDefaultAsync(d => d.Id == DelegationId.From(request.Id) && d.PrincipalUserId == userId, cancellationToken);
        if (delegation is null)
        {
            return CommonErrors.NotFound;
        }

        delegation.Revoke();
        return Result.Success();
    }
}
