using AppTemplate.Application.Core.Common.Results;
using FluentValidation;

namespace AppTemplate.Application.Core.Common.Validation;

/// <summary>
/// The one line a use case writes to run its validator, so no use case assembles a validation
/// <see cref="Error"/> of its own.
/// </summary>
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
