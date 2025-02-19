using System;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Commons.Services;

public static class ValidationService
{
    public static async Task Validate<T>(IValidator<T> validator ,T dto)
    {
        var _validator = await validator.ValidateAsync(dto);

        if (!_validator.IsValid)
        {
            throw new ValidationException(_validator.Errors);
        }
    }
}
