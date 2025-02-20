using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Validations;

public class LoginValidator : AbstractValidator<LoginRequestDto>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
