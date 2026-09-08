namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>Whether the second factor came off, with nothing else to report either way.</summary>
public sealed record TwoFactorDisableOutcome(TwoFactorDisableStatus Status)
{
    /// <summary>The account no longer demands a code at sign-in.</summary>
    public static TwoFactorDisableOutcome Disabled { get; } = new(TwoFactorDisableStatus.Disabled);

    /// <summary>The password did not match, so the second factor is untouched.</summary>
    public static TwoFactorDisableOutcome IncorrectPassword { get; } = new(TwoFactorDisableStatus.IncorrectPassword);
}
