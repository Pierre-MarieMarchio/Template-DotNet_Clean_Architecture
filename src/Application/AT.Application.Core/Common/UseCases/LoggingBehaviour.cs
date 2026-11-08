using System.Diagnostics;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Core.Common.UseCases;

public sealed class LoggingBehaviour<TMessage, TResponse>(ILoggerFactory loggerFactory)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private static readonly string _useCase = typeof(TMessage).Name;

    private readonly ILogger _logger = loggerFactory.CreateLogger(UseCaseInstruments.Name);

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        long started = Stopwatch.GetTimestamp();

        var response = await next(message, cancellationToken);

        if (response is Result { Error: { } error })
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Use case {UseCase} failed with {ErrorCode} in {ElapsedMilliseconds} ms",
                    _useCase,
                    error.Code,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Use case {UseCase} succeeded in {ElapsedMilliseconds} ms",
                _useCase,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return response;
    }
}
