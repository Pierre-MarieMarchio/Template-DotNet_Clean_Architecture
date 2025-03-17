namespace CA.Presentation.WebApi.Commons.Factories;

public static class CookieFactory
{
    public static CookieOptions CookieOptions(DateTime expirationDate)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            Expires = expirationDate,
            SameSite = SameSiteMode.Strict
        };
    }
}
