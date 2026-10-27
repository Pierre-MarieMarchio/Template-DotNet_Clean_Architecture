namespace AppTemplate.Client.Common.Errors;

internal sealed class UnreadableAnswerHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || IsJson(response.Content.Headers.ContentType?.MediaType))
        {
            return response;
        }

        response.Content.Dispose();
        response.Content = new ByteArrayContent([]);

        return response;
    }

    private static bool IsJson(string? mediaType) =>
        mediaType is not null
        && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
