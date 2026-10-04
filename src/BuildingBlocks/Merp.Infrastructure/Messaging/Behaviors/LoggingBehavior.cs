using System.Diagnostics;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>Outermost behavior: tracing span, duration and outcome of every request.</summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var requestName = typeof(TRequest).Name;
        using var activity = MediatorTelemetry.ActivitySource.StartActivity($"mediator {requestName}");
        activity?.SetTag("merp.request", typeof(TRequest).FullName);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var response = await next();
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            if (response.IsSuccess)
            {
                LogSucceeded(logger, requestName, elapsed);
            }
            else
            {
                activity?.SetTag("merp.error.code", response.Error.Code);
                LogFailed(logger, requestName, response.Error.Code, response.Error.Type, elapsed);
            }

            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            LogException(logger, exception, requestName, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} succeeded in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogSucceeded(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} failed with {ErrorCode} ({ErrorType}) in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode, ErrorType errorType, double elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "{RequestName} threw an exception after {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogException(ILogger logger, Exception exception, string requestName, double elapsedMilliseconds);
}
