using System;
using CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Modules.ItemModule.Validations;

public class ItemValidator : AbstractValidator<ItemCreateDto>
{
    public ItemValidator()
    {
        RuleFor(x => x.UserName)
           .NotEmpty().WithMessage("Le Username est requis.")
           .MaximumLength(100).WithMessage("Le Username ne doit pas dépasser 100 caractères.");

    }
}
