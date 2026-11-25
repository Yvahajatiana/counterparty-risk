using System.Diagnostics;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.Domain.Contracts.Results;
using Microsoft.Extensions.Logging;

namespace CounterpartyRisk.SharedKernel.Application.Behaviors;

/// <summary>
/// Outermost behavior: logs the request type, duration and outcome.
/// The request payload is never logged (it may carry amounts or counterparty identifiers).
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var requestName = typeof(TRequest).Name;
        var start = Stopwatch.GetTimestamp();
        LogHandling(requestName);

        try
        {
            var result = await continuation().ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (result.IsSuccess)
            {
                LogHandled(requestName, elapsedMs);
            }
            else
            {
                // Error codes are stable and non-sensitive by contract (see Error).
                LogFailed(requestName, result.Error.Code, elapsedMs);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up: expected behavior, not an application error.
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LogCancelled(requestName, elapsedMs);
            throw;
        }
        catch (Exception ex)
        {
            // Only the exception type is logged here; details go through the exception handler.
            LogException(requestName, ex.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    private partial void LogHandling(string requestName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMs} ms")]
    private partial void LogHandled(string requestName, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode} in {ElapsedMs} ms")]
    private partial void LogFailed(string requestName, string errorCode, double elapsedMs);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "{RequestName} threw {ExceptionType} after {ElapsedMs} ms")]
    private partial void LogException(string requestName, string exceptionType, double elapsedMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "{RequestName} cancelled after {ElapsedMs} ms")]
    private partial void LogCancelled(string requestName, double elapsedMs);
}
