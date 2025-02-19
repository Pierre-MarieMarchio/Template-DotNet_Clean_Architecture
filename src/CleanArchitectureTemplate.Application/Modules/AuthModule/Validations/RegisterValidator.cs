using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Validations;

public class RegisterValidator : AbstractValidator<RegisterDto>
{
    public RegisterValidator()
    {

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(36).WithMessage("Username must not exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.")
            .MaximumLength(36).WithMessage("Password must not exceed 100 characters.");
    }
}
