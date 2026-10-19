using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class UnlockAccountCommandValidator : AbstractValidator<UnlockAccountCommand>
{
    /// <summary>The target id is required.</summary>
    public UnlockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
