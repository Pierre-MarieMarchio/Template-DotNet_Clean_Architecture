using System.Globalization;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

internal static class EmailLinkFactory
{
    internal static string Create(Uri page, string email, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            page.AbsoluteUri,
            Uri.EscapeDataString(email),
            Uri.EscapeDataString(token));
}
