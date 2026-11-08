using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace AppTemplate.Application.Core.Common.UseCases;

public sealed class ValidationBehaviour<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
    where TResponse : IFailable<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(message, cancellationToken);

            failures.AddRange(validation.Errors);
        }

        return failures.Count == 0
            ? await next(message, cancellationToken)
            : TResponse.Failure(ValidationError.From(new ValidationResult(failures)));
    }
}
