using System.Web;
using AppTemplate.PresentationApp.Core.Common.Sessions;

namespace AppTemplate.PresentationApp.Core.UnitTests.TestSupport;

internal sealed class ScriptedBrowser(Func<string, string>? callbackQuery = null) : ISystemBrowser
{
    private static readonly HttpClient _http = new();

    private readonly Func<string, string> _callbackQuery = callbackQuery ?? (state => $"code=c1&state={Uri.EscapeDataString(state)}");

    public Uri? Opened { get; private set; }

    public Task<string>? Page { get; private set; }

    public void Open(Uri address)
    {
        Opened = address;
        var authorize = HttpUtility.ParseQueryString(address.Query);
        var redirect = new Uri(authorize["redirect_uri"]!);

        Page = _http.GetStringAsync(new Uri(redirect, $"?{_callbackQuery(authorize["state"]!)}"));
    }
}
