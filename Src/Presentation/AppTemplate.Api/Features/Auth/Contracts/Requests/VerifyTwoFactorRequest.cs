namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <param name="Code">Either the authenticator app's current six digits or one of the recovery codes.</param>
public sealed record VerifyTwoFactorRequest(string ChallengeToken, string Code);
