namespace AppTemplate.Application.Common;

/// <summary>The outcome of a use case: expected failures are values here, not exceptions.</summary>
public class Result
{
    private protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("A failed result requires an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>Non-null when <see cref="IsFailure"/> is true.</summary>
    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    /// <summary>Reports the same failure under another value type. Only valid on a failure.</summary>
    public Result<TOther> To<TOther>() => IsFailure
        ? Result<TOther>.Failure(Error!)
        : throw new InvalidOperationException("A success cannot be converted: it carries no error.");
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error? error) : base(isSuccess, error) => _value = value;

    /// <summary>
    /// Throws when the result is a failure. Property subpatterns short-circuit in the order they are
    /// written, so <c>is { Value: var x, IsSuccess: true }</c> reads this getter before
    /// <see cref="Result.IsSuccess"/> is checked and throws on a failure instead of failing to
    /// match — the reverse order is safe, but that is an easy detail to get backwards, so prefer
    /// checking <see cref="Result.IsFailure"/> on its own line instead of naming <see cref="Value"/>
    /// in a property pattern at all.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    /// <summary>
    /// A success must carry a value. Absence is a failure with an <see cref="Error"/>, not a
    /// success holding <c>null</c> — without this guard an adapter returning null under a
    /// non-nullable declaration produced <see cref="IsSuccess"/> with a null
    /// <see cref="Value"/>, which a controller serves as a 200 with an empty body.
    /// </summary>
    public static Result<TValue> Success(TValue value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(
                nameof(value),
                "A successful result must carry a value. Use Failure to report an absent one.");
        }

        return new(value, true, null);
    }

    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    /// <summary>Routes through <see cref="Success(TValue)"/>, so the null guard applies here too.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
