using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    /// <summary>The target id is required.</summary>
    public DeleteAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
