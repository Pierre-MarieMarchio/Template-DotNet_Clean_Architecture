using CA.Application.Modules.Auth.DTOs.Requests;
using FluentValidation;

namespace CA.Application.Modules.Auth.UseCases.Authenticate;

class AuthenticateValidator : AbstractValidator<AuthenticateRequest>
{
    public AuthenticateValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh Token is required.");

    }
}

