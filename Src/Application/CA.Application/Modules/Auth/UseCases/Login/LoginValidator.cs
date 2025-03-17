using CA.Application.Modules.Auth.DTOs.Requests;
using FluentValidation;

namespace CA.Application.Modules.Auth.UseCases.Login;

class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
