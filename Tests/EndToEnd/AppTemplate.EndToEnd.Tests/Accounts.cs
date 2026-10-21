using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppTemplate.EndToEnd.Tests;

internal sealed record Account(string Email, string Password);

internal sealed record SignedIn(string AccessToken, string RefreshToken);

internal static class Accounts
{
    private static readonly Regex _confirmationLink = new(
        "#email=(?<email>[^&\\s]+)&token=(?<token>[^\\s\"<>]+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static Account New() =>
        new($"end-to-end-{Guid.NewGuid():N}@example.com", Guid.NewGuid().ToString("N"));

    internal static async Task RegisterAndConfirmAsync(HttpClient api, Account account, CancellationToken cancellationToken)
    {
        using (var registered = await Wire.SendAsync(
            api, HttpMethod.Post, "api/v1/auth/register", cancellationToken, new { email = account.Email, password = account.Password }))
        {
            await Wire.ExpectAsync(registered, HttpStatusCode.Accepted, cancellationToken);
        }

        string mail = await Mailbox.ReadAsync(account.Email, cancellationToken);
        var link = _confirmationLink.Match(mail);

        if (!link.Success)
        {
            throw new InvalidOperationException($"The mail to {account.Email} holds no confirmation link: {mail}");
        }

        using var confirmed = await Wire.SendAsync(
            api,
            HttpMethod.Post,
            "api/v1/auth/confirm-email",
            cancellationToken,
            new { email = account.Email, token = Uri.UnescapeDataString(link.Groups["token"].Value) });

        await Wire.ExpectAsync(confirmed, HttpStatusCode.NoContent, cancellationToken);
    }

    internal static async Task<SignedIn> SignInAsync(HttpClient api, Account account, CancellationToken cancellationToken)
    {
        using var response = await Wire.SendAsync(
            api, HttpMethod.Post, "api/v1/auth/login", cancellationToken, new { email = account.Email, password = account.Password });

        var login = await Wire.ExpectJsonAsync(response, HttpStatusCode.OK, cancellationToken);

        return Pair(login.GetProperty("tokens"));
    }

    internal static SignedIn Pair(JsonElement tokens) =>
        new(Wire.Text(tokens, "accessToken"), Wire.Text(tokens, "refreshToken"));
}
