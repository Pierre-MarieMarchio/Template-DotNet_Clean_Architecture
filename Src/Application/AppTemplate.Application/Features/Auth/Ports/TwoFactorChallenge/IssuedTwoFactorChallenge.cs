namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;

/// <param name="ChallengeToken">
/// Identifies the pending sign-in for whichever second-step call redeems it. Not a bearer
/// credential: on its own it proves nothing but that a password was already verified, and it is
/// worthless without the code that goes with it.
/// </param>
public sealed record IssuedTwoFactorChallenge(string ChallengeToken, DateTimeOffset ExpiresAt);
