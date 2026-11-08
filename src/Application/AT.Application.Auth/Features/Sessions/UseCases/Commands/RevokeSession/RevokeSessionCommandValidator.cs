using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}
