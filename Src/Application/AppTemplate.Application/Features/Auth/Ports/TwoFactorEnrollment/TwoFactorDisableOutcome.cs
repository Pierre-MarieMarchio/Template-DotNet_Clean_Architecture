namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

public sealed record TwoFactorDisableOutcome(TwoFactorDisableStatus Status)
{
    public static TwoFactorDisableOutcome Disabled { get; } = new(TwoFactorDisableStatus.Disabled);

    public static TwoFactorDisableOutcome IncorrectPassword { get; } = new(TwoFactorDisableStatus.IncorrectPassword);
}
