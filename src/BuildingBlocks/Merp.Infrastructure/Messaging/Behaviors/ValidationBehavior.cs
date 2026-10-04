using FluentValidation;
using FluentValidation.Results;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>Runs FluentValidation validators; failures become an <see cref="Error.Validation(IEnumerable{FieldError})"/> result.</summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        var fields = failures.Select(ValidationFailureMapper.ToFieldError).ToList();
        return ResultFactory.Failure<TResponse>(Error.Validation(fields));
    }
}
