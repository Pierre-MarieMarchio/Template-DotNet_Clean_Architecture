namespace AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;

public sealed class ExternalIdentityProviderOptions
{
    public string Name { get; set; } = string.Empty;

    public IList<string> Issuers { get; } = [];

    public IList<string> Audiences { get; } = [];

    public string JwksUri { get; set; } = string.Empty;

    public string MetadataAddress { get; set; } = string.Empty;
}
