using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Options;

/// <summary>
/// How long a token minted by ASP.NET Identity's default token provider stays valid.
/// <para>
/// <c>AddDefaultTokenProviders</c> wires up <c>DataProtectorTokenProvider</c> for both the
/// "Default" and "Email" purposes, and both resolve the same un-named
/// <c>IOptions&lt;DataProtectionTokenProviderOptions&gt;</c> — there is one lifespan for every
/// provider it registers, not one per purpose. Today that only governs email confirmation; once a
/// password-reset token exists it will share this same setting unless it is given a token provider
/// of its own. This class exists so that day's implementer configures a value instead of
/// discovering the framework's one-day default the hard way.
/// </para>
/// </summary>
public sealed class IdentityTokenOptions
{
    public const string SectionName = "IdentityTokens";

    public TimeSpan Lifespan { get; set; } = TimeSpan.FromDays(1);
}

internal sealed class IdentityTokenOptionsValidator : IValidateOptions<IdentityTokenOptions>
{
    private static readonly TimeSpan _minimumLifespan = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _maximumLifespan = TimeSpan.FromDays(30);

    public ValidateOptionsResult Validate(string? name, IdentityTokenOptions options) =>
        options.Lifespan < _minimumLifespan || options.Lifespan > _maximumLifespan
            ? ValidateOptionsResult.Fail(
                $"'{IdentityTokenOptions.SectionName}:Lifespan' must be between {_minimumLifespan} and " +
                $"{_maximumLifespan}.")
            : ValidateOptionsResult.Success;
}
