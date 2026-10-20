using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Application.Core.Common.Validation;

public static class DomainGuard
{
    public static Result Try(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            operation();

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(CommonErrors.InvariantViolated(exception.Message));
        }
    }

    public static Result<TValue> Try<TValue>(Func<TValue> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return Result.Success(operation());
        }
        catch (DomainException exception)
        {
            return Result.Failure<TValue>(CommonErrors.InvariantViolated(exception.Message));
        }
    }
}
