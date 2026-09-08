using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Concurrency;

/// <summary>
/// The conflicts a caller's own version precondition can produce, kept apart from the races a caller
/// could not have foreseen.
/// </summary>
public static class ConcurrencyErrors
{
    /// <summary>
    /// The caller's change was decided against a version the aggregate no longer holds. Deliberately
    /// distinct from <c>concurrency.conflict</c>, which is a race the caller could not have seen:
    /// this one means the caller was working from a stale copy and has to read again.
    /// </summary>
    public static readonly Error PreconditionFailed = Error.PreconditionFailed(
        "precondition.failed",
        "The resource has changed since the version this request names. Read it again and retry.");
}
