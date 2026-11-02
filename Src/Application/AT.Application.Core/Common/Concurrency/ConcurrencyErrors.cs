using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Concurrency;

public static class ConcurrencyErrors
{
    public static readonly Error PreconditionFailed = Error.PreconditionFailed(
        "precondition.failed",
        "The resource has changed since the version this request names. Read it again and retry.");
}
