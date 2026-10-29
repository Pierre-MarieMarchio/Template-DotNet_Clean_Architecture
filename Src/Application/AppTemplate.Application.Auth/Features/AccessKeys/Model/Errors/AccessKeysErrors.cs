using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;

public static class AccessKeysErrors
{
    public static Error InvalidKey { get; } = Error.Unauthorized(
        "auth.accessKey.invalid",
        "The access key is invalid, revoked or expired.");

    public static Error NoSuchAccessKey { get; } = Error.NotFound(
        "auth.accessKey.notFound",
        "No such access key.");

    public static Error TooManyAccessKeys { get; } = Error.Conflict(
        "auth.accessKey.limitReached",
        "The account holds as many live access keys as it may; revoke one first.");

    public static Error ExpiryNotInTheFuture { get; } = ValidationError.ForField(
        "expiresAt",
        "The expiry must be in the future.");
}
