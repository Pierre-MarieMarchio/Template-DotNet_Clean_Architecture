using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Application.Core.Common.Validation;

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

    /// <summary>
    /// The value-carrying counterpart, catching exactly as narrowly: anything that is not a domain
    /// invariant keeps propagating.
    /// </summary>
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
