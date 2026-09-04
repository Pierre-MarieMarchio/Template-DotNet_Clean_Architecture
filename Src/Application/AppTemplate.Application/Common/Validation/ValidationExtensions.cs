using FluentValidation;

namespace AppTemplate.Application.Common.Validation;

public static class ValidationExtensions
{
    /// <summary>Runs <paramref name="validator"/> and turns a failure into a <see cref="ValidationError"/>.</summary>
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
