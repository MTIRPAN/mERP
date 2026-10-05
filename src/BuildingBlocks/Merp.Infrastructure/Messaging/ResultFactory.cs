using System.Collections.Concurrent;
using System.Reflection;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging;

/// <summary>Creates failed <see cref="Result"/> or <see cref="Result{TValue}"/> instances for a generic response type.</summary>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> Factories = new();

    public static TResponse Failure<TResponse>(Error error)
        where TResponse : Result
    {
        var factory = Factories.GetOrAdd(typeof(TResponse), static type => CreateFactory(type));
        return (TResponse)factory(error);
    }

    private static Func<Error, Result> CreateFactory(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return Result.Failure;
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = responseType.GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static, [typeof(Error)])!;
            return method.CreateDelegate<Func<Error, Result>>();
        }

        throw new InvalidOperationException($"Unsupported response type {responseType.FullName}.");
    }
}
