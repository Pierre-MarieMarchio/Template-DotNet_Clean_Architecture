using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Features.Auth.Composition;

internal static class BrowserSession
{
    public const string ModeHeader = "X-Refresh-Token-Mode";

    public const string CookieMode = "cookie";

    public const string ModePattern = "^" + CookieMode + "$";

    public const string Name = "refresh_token";

    public const string Path = "/api/v1/auth";

    public static bool IsRequested(string? mode) => string.Equals(mode, CookieMode, StringComparison.Ordinal);

    public static string? Presented(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies[Name] is { Length: > 0 } token ? token : null;
    }

    public static Result<LoginResponse> Deliver(HttpContext httpContext, string? mode, Result<LoginResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return IsRequested(mode) && result is { IsSuccess: true, Value: LoginResponse.Authenticated authenticated }
            ? Result.Success<LoginResponse>(authenticated with { Tokens = Hold(httpContext, authenticated.Tokens) })
            : result;
    }

    public static Result<ExternalLoginResponse> Deliver(
        HttpContext httpContext,
        string? mode,
        Result<ExternalLoginResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return IsRequested(mode) && result is { IsSuccess: true, Value: ExternalLoginResponse.Authenticated authenticated }
            ? Result.Success<ExternalLoginResponse>(authenticated with { Tokens = Hold(httpContext, authenticated.Tokens) })
            : result;
    }

    public static Result<TokenResponse> Rotate(HttpContext httpContext, Result<TokenResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return Result.Success(Hold(httpContext, result.Value));
        }

        if (result.Error?.Type == ErrorType.Unauthorized)
        {
            Clear(httpContext);
        }

        return result;
    }

    public static void Clear(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Cookies.Delete(Name, Options(httpContext.Request, expires: null));
    }

    private static TokenResponse Hold(HttpContext httpContext, TokenResponse tokens)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Cookies.Append(
            Name,
            tokens.RefreshToken,
            Options(httpContext.Request, tokens.RefreshTokenExpiresAt));

        return tokens with { RefreshToken = null! };
    }

    private static CookieOptions Options(HttpRequest request, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = request.PathBase.Add(new PathString(Path)).Value,
        Expires = expires,
        IsEssential = true,
    };
}
