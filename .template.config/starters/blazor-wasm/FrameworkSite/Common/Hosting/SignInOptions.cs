namespace AppTemplate.FrameworkSite.Common.Hosting;

public sealed class SignInOptions
{
    public const string SectionName = "SignIn";

    public Uri? Authority { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string Scope { get; set; } = "openid profile email";

    public bool RequireHttpsMetadata { get; set; } = true;
}
