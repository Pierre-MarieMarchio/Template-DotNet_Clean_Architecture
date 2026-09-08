namespace AppTemplate.Application.Core.Common.Results;

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

    /// <summary>Whether the use case produced what it was asked for.</summary>
    public bool IsSuccess { get; }

    /// <summary>The negation, so a guard clause reads as one.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Non-null when <see cref="IsFailure"/> is true.</summary>
    public Error? Error { get; }

    /// <summary>An operation that did its work and has nothing to hand back.</summary>
    public static Result Success() => new(true, null);

    /// <summary>An expected refusal. This, and not a thrown exception, is how a use case says no.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Lives here rather than on the generic type so <typeparamref name="TValue"/> is inferred and no
    /// caller has to name it (CA1000).
    /// </summary>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>
    /// The failing counterpart, for an operation whose success would have carried a value. The type
    /// argument has to be named here: there is no value to infer it from.
    /// </summary>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    /// <summary>Reports the same failure under another value type. Only valid on a failure.</summary>
    public Result<TOther> To<TOther>() => IsFailure
        ? Result<TOther>.Failure(Error!)
        : throw new InvalidOperationException("A success cannot be converted: it carries no error.");
}

/// <summary>
/// A result that carries a value when it succeeds. A success always has one: absence is a failure,
/// never a success holding <c>null</c>.
/// </summary>
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
    /// non-nullable declaration produced <see cref="Result.IsSuccess"/> with a null
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

    /// <summary>
    /// Hides the base's non-generic <c>Failure</c>, so a failure written inside this type still types as
    /// a result of the same value type.
    /// </summary>
    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    /// <summary>Routes through <see cref="Success(TValue)"/>, so the null guard applies here too.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
