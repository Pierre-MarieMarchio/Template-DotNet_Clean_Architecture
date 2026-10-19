using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    /// <summary>The session id is required.</summary>
    public RevokeSessionCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}
