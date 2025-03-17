namespace CA.Infrastructure.Identity.Settings;

#pragma warning disable
public class JwtSettings
{
    public string Key { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public bool RequireHttpsMetadata { get; set; }
    public double DurationInMinutes { get; set; }
}
