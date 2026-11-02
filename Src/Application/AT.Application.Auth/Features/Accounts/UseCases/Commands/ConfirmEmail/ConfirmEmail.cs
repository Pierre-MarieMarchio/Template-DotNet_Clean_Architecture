using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Email, string Token) : ICommand<Result>;

public sealed class ConfirmEmailCommandHandler(
    IEmailTokensService emailTokens) : ICommandHandler<ConfirmEmailCommand, Result>
{
    public async ValueTask<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool confirmed = await emailTokens.ConfirmEmailAsync(request.Email, request.Token, cancellationToken);

        return confirmed
            ? Result.Success()
            : Result.Failure(AccountsErrors.InvalidEmailConfirmation);
    }
}
