using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LockAccount;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class LockAccountCommandValidator : AbstractValidator<LockAccountCommand>
{
    /// <summary>The target id is required.</summary>
    public LockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
