using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Common.Concurrency;

internal static class PreconditionErrors
{
    public static readonly Error Required = Error.PreconditionRequired(
        "precondition.required",
        "This operation requires an 'If-Match' header carrying the entity tag of the version the "
        + "request was decided against.");

    public static readonly Error Malformed = Error.Validation(
        "precondition.malformed",
        "The 'If-Match' header is not '*' or a comma-separated list of quoted entity tags.");
}
