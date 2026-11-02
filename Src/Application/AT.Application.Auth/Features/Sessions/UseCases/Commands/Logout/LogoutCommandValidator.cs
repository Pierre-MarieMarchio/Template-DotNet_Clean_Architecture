using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
}
