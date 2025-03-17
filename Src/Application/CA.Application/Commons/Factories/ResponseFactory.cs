namespace CA.Application.Commons.Factories;

public static class ResponseFactory
{
    public static TResponse CreateResponse<TResponse>(object result)
        where TResponse : class
    {
        return Activator.CreateInstance(typeof(TResponse), result) as TResponse
            ?? throw new InvalidOperationException("Failed to create response object.");
    }

    public static List<TResponse> CreateResponseList<TResponse>(IEnumerable<object> result)
        where TResponse : class
    {
        var response = result.Select(entity => Activator.CreateInstance(typeof(TResponse), entity) as TResponse).ToList();
        return response as List<TResponse> ?? throw new InvalidOperationException("Failed to create response object.");
    }
}
