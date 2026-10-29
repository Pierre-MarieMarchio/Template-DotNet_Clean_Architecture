using System.Net;
using System.Text;
using System.Text.Json;

namespace AppTemplate.PresentationApp.Core.UnitTests.TestSupport;

internal sealed class StubProvider(params HttpResponseMessage[] tokenAnswers)
{
    public const string Authority = "https://login.test";

    public const string TokenPath = "/token";

    private readonly Queue<HttpResponseMessage> _tokenAnswers = new(tokenAnswers);

    public List<string> TokenRequests { get; } = [];

    public static HttpResponseMessage Tokens(string accessToken, string? refreshToken = null, int expiresIn = 3600, string name = "Ada Lovelace") =>
        StubApi.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["access_token"] = accessToken,
            ["refresh_token"] = refreshToken,
            ["expires_in"] = expiresIn,
            ["token_type"] = "Bearer",
            ["id_token"] = IdentityToken(name),
        }));

    public static HttpResponseMessage Refused(string error) =>
        StubApi.Json(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = error }));

    public HttpResponseMessage? Answer(HttpRequestMessage request)
    {
        if (request.RequestUri is not { Host: "login.test" } address)
        {
            return null;
        }

        switch (address.AbsolutePath)
        {
            case "/.well-known/openid-configuration":
                return StubApi.Json(HttpStatusCode.OK, $$"""
                    {"issuer":"{{Authority}}","authorization_endpoint":"{{Authority}}/authorize","token_endpoint":"{{Authority}}{{TokenPath}}","jwks_uri":"{{Authority}}/jwks"}
                    """);
            case "/jwks":
                return StubApi.Json(HttpStatusCode.OK, """{"keys":[]}""");
            case TokenPath:
                using (var body = new StreamReader(request.Content!.ReadAsStream()))
                {
                    TokenRequests.Add(body.ReadToEnd());
                }

                return _tokenAnswers.Dequeue();
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static string IdentityToken(string name) =>
        $"{Base64Url("""{"alg":"none"}""")}.{Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Authority,
            ["aud"] = PresentationHost.ClientId,
            ["sub"] = "subject-1",
            ["name"] = name,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }))}.";

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
