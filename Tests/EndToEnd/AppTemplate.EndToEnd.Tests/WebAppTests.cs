using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace AppTemplate.EndToEnd.Tests;

public sealed class WebAppTests
{
    private static readonly Regex _script = new(
        @"<script\b(?<attributes>[^>]*)>(?<body>[\s\S]*?)</script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _source = new(
        @"\ssrc\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _dataBlock = new(
        @"\stype\s*=\s*[""']?application/(ld\+)?json",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _nonce = new(
        @"\snonce\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static TheoryData<string> WebAppPorts => [.. Stack.WebAppPortSettings];

    [Theory]
    [MemberData(nameof(WebAppPorts))]
    public async Task AWebApp_ServesItsPageAndRelaysApiToTheApi(string portSetting)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var site = Stack.Client(Stack.WebApp(portSetting));

        using (var page = await Wire.SendAsync(site, HttpMethod.Get, string.Empty, cancellationToken))
        {
            (await Wire.ExpectAsync(page, HttpStatusCode.OK, cancellationToken)).ShouldContain("<html", Case.Insensitive);
        }

        using var relayed = await Wire.SendAsync(site, HttpMethod.Get, Refusal.Path, cancellationToken);

        var problem = await Wire.ExpectJsonAsync(relayed, Refusal.Status, cancellationToken);
        relayed.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        Wire.Text(problem, "code").ShouldBe(Refusal.Code);
    }

    [Theory]
    [MemberData(nameof(WebAppPorts))]
    public async Task AWebAppsPage_RunsAnInlineScriptOnlyByItsHashOrItsResponsesNonce_AndNeverRepeatsANonce(string portSetting)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var site = Stack.Client(Stack.WebApp(portSetting));

        var first = await PageAsync(site, cancellationToken);
        var second = await PageAsync(site, cancellationToken);

        foreach (var (html, allowed) in new[] { first, second })
        {
            string[] refused = [.. InlineScripts(html)
                .Where(script => !allowed.Contains(script.Hash, StringComparer.Ordinal) && !allowed.Contains(script.Nonce, StringComparer.Ordinal))
                .Select(script => script.Hash)];

            refused.ShouldBeEmpty($"{site.BaseAddress} serves inline scripts its script-src allows neither by hash nor by nonce.");
        }

        Nonces(first.Allowed).Intersect(Nonces(second.Allowed), StringComparer.Ordinal).ShouldBeEmpty(
            $"{site.BaseAddress} sent one nonce in two responses, so a nonce no longer proves a script came with its page.");
    }

    //#if (localAuth)
    [Theory]
    [MemberData(nameof(WebAppPorts))]
    public async Task AServerRenderedApp_RefusesACallThatChangesStateWithoutXCsrf_WhereASinglePageAppRelaysIt(string portSetting)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var site = Stack.Client(Stack.WebApp(portSetting));
        bool serverRendered = Nonces((await PageAsync(site, cancellationToken)).Allowed).Any();

        using var unmarked = await Wire.SendAsync(site, HttpMethod.Post, Refusal.Path, cancellationToken);
        using var marked = new HttpRequestMessage(HttpMethod.Post, new Uri(Refusal.Path, UriKind.Relative));
        marked.Headers.Add("X-CSRF", "1");
        using var relayed = await site.SendAsync(marked, cancellationToken);

        var refusal = await Wire.ExpectJsonAsync(unmarked, serverRendered ? HttpStatusCode.Forbidden : Refusal.Status, cancellationToken);
        Wire.Text(refusal, "code").ShouldBe(serverRendered ? "session.csrfHeaderRequired" : Refusal.Code);
        Wire.Text(await Wire.ExpectJsonAsync(relayed, Refusal.Status, cancellationToken), "code").ShouldBe(Refusal.Code);
    }

    //#endif
    private static async Task<(string Html, string[] Allowed)> PageAsync(HttpClient site, CancellationToken cancellationToken)
    {
        using var page = await Wire.SendAsync(site, HttpMethod.Get, string.Empty, cancellationToken);
        string html = await Wire.ExpectAsync(page, HttpStatusCode.OK, cancellationToken);
        string policy = page.Headers.TryGetValues("Content-Security-Policy", out var values) ? string.Join(';', values) : string.Empty;
        string[] allowed = [.. policy.Split(';')
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.FirstOrDefault() == "script-src")
            .SelectMany(words => words.Skip(1))];

        return (html, allowed);
    }

    private static IEnumerable<(string Hash, string Nonce)> InlineScripts(string html) =>
        _script.Matches(html)
            .Where(script => !_source.IsMatch(script.Groups["attributes"].Value)
                && !_dataBlock.IsMatch(script.Groups["attributes"].Value)
                && script.Groups["body"].Value.Trim().Length > 0)
            .Select(script => (
                $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script.Groups["body"].Value)))}'",
                _nonce.Match(script.Groups["attributes"].Value) is { Success: true } nonce ? $"'nonce-{nonce.Groups[1].Value}'" : string.Empty));

    private static IEnumerable<string> Nonces(string[] allowed) =>
        allowed.Where(source => source.StartsWith("'nonce-", StringComparison.Ordinal));
}
