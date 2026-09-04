namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

public sealed record TwoFactorDisable(TwoFactorDisableOutcome Outcome)
{
    public static TwoFactorDisable Disabled { get; } = new(TwoFactorDisableOutcome.Disabled);

    public static TwoFactorDisable IncorrectPassword { get; } = new(TwoFactorDisableOutcome.IncorrectPassword);
}
