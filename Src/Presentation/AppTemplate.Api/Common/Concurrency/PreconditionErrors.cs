using AppTemplate.Application.Common.Results;

namespace AppTemplate.Api.Common.Concurrency;

/// <summary>
/// Verdicts on the <c>If-Match</c> header itself: transport, not domain. The application layer
/// never sees a raw header, so it cannot be the one to author these two.
/// </summary>
public static class PreconditionErrors
{
    /// <summary>
    /// Only reachable where unconditional writes are refused by configuration; the message names the
    /// header, because a client that never sent one has no other way to learn what is missing.
    /// </summary>
    public static readonly Error Required = Error.PreconditionRequired(
        "precondition.required",
        "This operation requires an 'If-Match' header carrying the entity tag of the version the "
        + "request was decided against.");

    public static readonly Error Malformed = Error.Validation(
        "precondition.malformed",
        "The 'If-Match' header is not '*' or a comma-separated list of quoted entity tags.");
}
