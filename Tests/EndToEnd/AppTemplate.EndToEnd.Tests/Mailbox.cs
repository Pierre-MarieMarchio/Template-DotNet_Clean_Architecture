using System.Net;

namespace AppTemplate.EndToEnd.Tests;

internal static class Mailbox
{
    internal static async Task<string> ReadAsync(string recipient, CancellationToken cancellationToken)
    {
        using var mailpit = Stack.Client(Stack.Mailpit);

        string id = await Poll.UntilAsync(
            $"A mail to {recipient}",
            TimeSpan.FromSeconds(30),
            async token =>
            {
                using var response = await mailpit.GetAsync(
                    new Uri($"api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", UriKind.Relative),
                    token);

                var found = await Wire.ExpectJsonAsync(response, HttpStatusCode.OK, token);
                var messages = found.GetProperty("messages");

                return messages.GetArrayLength() > 0 ? Wire.Text(messages[0], "ID") : null;
            },
            cancellationToken);

        using var message = await mailpit.GetAsync(new Uri($"api/v1/message/{id}", UriKind.Relative), cancellationToken);

        var read = await Wire.ExpectJsonAsync(message, HttpStatusCode.OK, cancellationToken);

        return Wire.Text(read, "Text") + Environment.NewLine + WebUtility.HtmlDecode(Wire.Text(read, "HTML"));
    }
}
