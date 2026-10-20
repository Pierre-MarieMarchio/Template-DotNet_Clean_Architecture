using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;

public sealed class ConfirmEmailUseCase(
    IEmailTokensService emailTokens,
    IValidator<ConfirmEmailCommand> validator) : IConfirmEmailUseCase
{
    public async Task<Result> ExecuteAsync(ConfirmEmailCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        bool confirmed = await emailTokens.ConfirmEmailAsync(request.Email, request.Token, cancellationToken);

        return confirmed
            ? Result.Success()
            : Result.Failure(AccountsErrors.InvalidEmailConfirmation);
    }
}
