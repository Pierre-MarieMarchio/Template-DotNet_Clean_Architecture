namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>Whether the second factor came off, with nothing else to report either way.</summary>
public sealed record TwoFactorDisableOutcome(TwoFactorDisableStatus Status)
{
    /// <summary>The account no longer demands a code at sign-in.</summary>
    public static TwoFactorDisableOutcome Disabled { get; } = new(TwoFactorDisableStatus.Disabled);

    /// <summary>The password did not match, so the second factor is untouched.</summary>
    public static TwoFactorDisableOutcome IncorrectPassword { get; } = new(TwoFactorDisableStatus.IncorrectPassword);

    /// <summary>The store refused the change, so the second factor is still on.</summary>
    public static TwoFactorDisableOutcome Rejected { get; } = new(TwoFactorDisableStatus.Rejected);

    /// <summary>Nothing changed — see <see cref="TwoFactorDisableStatus.NoSuchAccount"/>.</summary>
    public static TwoFactorDisableOutcome NoSuchAccount { get; } = new(TwoFactorDisableStatus.NoSuchAccount);
}
