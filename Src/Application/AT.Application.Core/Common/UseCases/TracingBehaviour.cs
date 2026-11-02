using System.Diagnostics;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Core.Common.UseCases;

public sealed class TracingBehaviour<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private static readonly string _useCase = typeof(TMessage).Name;

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        using var activity = UseCaseInstruments.ActivitySource.StartActivity(_useCase);
        activity?.SetTag(UseCaseInstruments.UseCaseTag, _useCase);

        long started = Stopwatch.GetTimestamp();
        string outcome = "exception";

        try
        {
            var response = await next(message, cancellationToken);

            if (response is Result { Error: { } error })
            {
                outcome = "failure";
                activity?.SetTag(UseCaseInstruments.ErrorCodeTag, error.Code);
            }
            else
            {
                outcome = "success";
            }

            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);

            throw;
        }
        finally
        {
            activity?.SetTag(UseCaseInstruments.OutcomeTag, outcome);

            UseCaseInstruments.Duration.Record(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>(UseCaseInstruments.UseCaseTag, _useCase),
                new KeyValuePair<string, object?>(UseCaseInstruments.OutcomeTag, outcome));
        }
    }
}
