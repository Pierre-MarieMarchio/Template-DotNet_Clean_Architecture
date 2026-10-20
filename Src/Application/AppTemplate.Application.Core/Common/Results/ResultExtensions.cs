namespace AppTemplate.Application.Core.Common.Results;

public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> project)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(project);

        return result.IsFailure
            ? result.To<TOut>()
            : project(result.Value);
    }
}
