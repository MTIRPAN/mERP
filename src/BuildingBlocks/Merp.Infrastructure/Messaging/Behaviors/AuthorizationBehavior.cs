using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>Checks <see cref="IRequirePermission"/>; system callers (background jobs) are trusted.</summary>
internal sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser, IPermissionChecker permissionChecker)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is IRequirePermission secured && !currentUser.IsSystem
            && !await permissionChecker.HasPermissionAsync(secured.Permission, cancellationToken))
        {
            return ResultFactory.Failure<TResponse>(CommonErrors.Forbidden);
        }

        return await next();
    }
}
