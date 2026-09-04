namespace AppTemplate.Application.Common.Results;

/// <summary>
/// Lifting a projection over a <see cref="Result{TValue}"/>: keep the failure, or apply the
/// function to the value.
/// </summary>
/// <remarks>
/// The failure guard cannot be folded into a property subpattern, and the reason is worth stating
/// once here rather than at every call site: <see cref="Result{TValue}.Value"/> throws on a failure,
/// so <c>is { IsSuccess: true, Value: var x }</c> throws instead of failing to match. The failure
/// has to be answered before anything reads the value.
/// </remarks>
public static class ResultExtensions
{
    /// <summary>
    /// Applies <paramref name="project"/> to the value of a successful result, or reports the same
    /// failure under the projected type.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Either argument is <c>null</c>. Thrown rather than treated as a failure: a missing result or
    /// a missing projection is a bug in the caller, not an expected outcome, and only expected
    /// outcomes travel as a <see cref="Result"/>.
    /// </exception>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> project)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(project);

        return result.IsFailure
            ? result.To<TOut>()
            : project(result.Value);
    }
}
