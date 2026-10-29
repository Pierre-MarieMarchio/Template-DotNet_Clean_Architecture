using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppPortal.Features.ExternalIdentity.Configuration.Options;

public sealed class AuthenticationExternalInteractiveOptions
{
    public const string SectionName = "Authentication:External:Interactive";

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }
}

internal sealed class AuthenticationExternalInteractiveOptionsValidator : IValidateOptions<AuthenticationExternalInteractiveOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationExternalInteractiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.ClientId)
            ? ValidateOptionsResult.Fail(
                $"'{AuthenticationExternalInteractiveOptions.SectionName}:{nameof(options.ClientId)}' is required: the provider knows this app by it.")
            : ValidateOptionsResult.Success;
    }
}
