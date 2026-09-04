using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Application.Common;

/// <summary>Turns a domain invariant violation into a failed <see cref="Result"/>.</summary>
public static class DomainGuard
{
    /// <summary>
    /// Runs <paramref name="operation"/>, catching only <see cref="DomainException"/>: any other
    /// exception, including <see cref="OperationCanceledException"/>, is a bug or a cancellation
    /// and must keep propagating.
    /// </summary>
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
