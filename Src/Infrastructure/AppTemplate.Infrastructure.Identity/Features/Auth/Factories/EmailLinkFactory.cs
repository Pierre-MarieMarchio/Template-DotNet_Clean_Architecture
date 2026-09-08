using System.Globalization;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>The link an account mail carries, and where its single-use token is put.</summary>
internal static class EmailLinkFactory
{
    /// <summary>
    /// Parameters URL-encoded and carried in the fragment rather than the query, so the token never
    /// reaches a server log, a browser history entry or a <c>Referer</c> header.
    /// </summary>
    internal static string Create(Uri page, string email, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            page.AbsoluteUri,
            Uri.EscapeDataString(email),
            Uri.EscapeDataString(token));
}
