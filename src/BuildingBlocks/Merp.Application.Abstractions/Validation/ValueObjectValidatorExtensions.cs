using FluentValidation;
using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Validation;

public static class ValueObjectValidatorExtensions
{
    /// <summary>
    /// Validates a raw input with a value object factory (e.g. <c>Tckn.Create</c>) and reports the factory's error
    /// code, so validators and domain share one rule and one message.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, string?> MustBeValid<T, TValue>(
        this IRuleBuilder<T, string?> ruleBuilder,
        Func<string?, Result<TValue>> factory)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(factory);

        return ruleBuilder.Custom((value, context) =>
        {
            if (value is null)
            {
                return;
            }

            var result = factory(value);
            if (result.IsFailure)
            {
                context.AddFailure(new FluentValidation.Results.ValidationFailure(context.PropertyPath, result.Error.Code)
                {
                    ErrorCode = result.Error.Code,
                });
            }
        });
    }
}
