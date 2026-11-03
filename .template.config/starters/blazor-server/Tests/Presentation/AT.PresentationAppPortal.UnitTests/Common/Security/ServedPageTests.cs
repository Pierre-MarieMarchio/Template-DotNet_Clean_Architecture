using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Common.Security;

public sealed partial class ServedPageTests
{
    [Fact]
    public async Task AServedPage_HoldsNoInlineScript()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string page = await client.GetStringAsync(new Uri("/not-found", UriKind.Relative), TestContext.Current.CancellationToken);

        page.ShouldContain("<html");
        InlineScript().Matches(page).Select(match => match.Value).ShouldBeEmpty(
            "The policy allows no inline script, so the browser would refuse each one named here.");
    }

    [GeneratedRegex(@"<script(?![^>]*\ssrc\s*=)[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex InlineScript();
}
