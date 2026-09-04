namespace AppTemplate.Application.Common.Results;

/// <summary>
/// Lifting a projection over a <see cref="Result{TValue}"/>: keep the failure, or apply the
/// function to the value.
/// </summary>
/// <remarks>
/// This exists because the Api layer was writing it out by hand thirteen times, in all four of its
/// verticals — a null check, an <see cref="Result.IsFailure"/> guard returning
/// <see cref="Result.To{TOther}"/>, and one line of real work. That is the shape a mapping method
/// has when the only thing it adds is the projection, and the count is what makes it worth naming:
/// four independent features arriving at the same seven lines is a demonstration, not a
/// resemblance.
/// <para>
/// The guard cannot be folded into a subpattern, and the reason is worth stating once here rather
/// than in a comment at every call site: <see cref="Result{TValue}.Value"/> throws on a failure, so
/// the failure has to be answered before anything reads the value.
/// </para>
/// </remarks>
public static class ResultExtensions
{
    /// <summary>
    /// Applies <paramref name="project"/> to the value of a successful result, or reports the same
    /// failure under the projected type.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Either argument is <c>null</c>. Thrown rather than treated as a failure: a missing result or
    /// a missing projection is a bug in the caller, not an expected outcome
    /// (<c>docs/adr/0004-result-as-the-failure-channel.md</c>).
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
