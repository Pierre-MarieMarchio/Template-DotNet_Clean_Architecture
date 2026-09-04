using AppTemplate.Application.Common;
using FluentValidation.Results;

namespace AppTemplate.Application.Features.Auth.Validators;

/// <summary>
/// Use cases check validation themselves rather than relying on an API-boundary filter, so the
/// rules hold for every caller — tests and background jobs included.
/// </summary>
internal static class ValidationResultExtensions
{
    internal const string ErrorCode = "auth.validation";

    internal static Error ToError(this ValidationResult validationResult) =>
        Error.Validation(ErrorCode, string.Join(" ", validationResult.Errors.Select(failure => failure.ErrorMessage)));
}
