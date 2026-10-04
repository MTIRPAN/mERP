using System.Diagnostics.CodeAnalysis;

namespace Merp.SharedKernel.Results;

/// <summary>Outcome of an operation that can fail with an expected <see cref="Results.Error"/>.</summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error) => _error = error;

    [MemberNotNullWhen(false, nameof(ErrorOrNull))]
    public bool IsSuccess => _error is null;

    [MemberNotNullWhen(true, nameof(ErrorOrNull))]
    public bool IsFailure => _error is not null;

    /// <summary>The error of a failed result. Throws for a successful result.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public Error? ErrorOrNull => _error;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome of an operation that returns a value or fails with an expected <see cref="Error"/>.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value of a successful result. Throws for a failed result.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value (error: {Error.Code}).");

    public static Result<TValue> Success(TValue value) => new(value, null);

    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Success(map(Value)) : Result<TOut>.Failure(Error);
    }

    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(Value) : Result<TOut>.Failure(Error);
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
