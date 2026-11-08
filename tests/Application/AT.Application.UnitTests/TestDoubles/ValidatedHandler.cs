using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using FluentValidation;
using Mediator;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class ValidatedHandler<TMessage, TResponse>(
    MessageHandlerDelegate<TMessage, TResponse> handler,
    IValidator<TMessage> validator)
    where TMessage : notnull, IMessage
    where TResponse : IFailable<TResponse>
{
    public ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken) =>
        new ValidationBehaviour<TMessage, TResponse>([validator]).Handle(message, handler, cancellationToken);
}
