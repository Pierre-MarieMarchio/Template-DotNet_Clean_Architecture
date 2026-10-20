using AppTemplate.Application.Core.Common.Results;
using FluentValidation;

namespace AppTemplate.Application.Core.Common.Validation;

public static class ValidationExtensions
{
    public static async Task<Result> EnsureValidAsync<TRequest>(
        this IValidator<TRequest> validator,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        return validation.IsValid ? Result.Success() : Result.Failure(ValidationError.From(validation));
    }
}
