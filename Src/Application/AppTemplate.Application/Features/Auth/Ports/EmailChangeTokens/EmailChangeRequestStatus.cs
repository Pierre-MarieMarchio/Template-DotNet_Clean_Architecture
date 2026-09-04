namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

public enum EmailChangeRequestStatus
{
    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectCurrentPassword,

    /// <summary>
    /// The current password matched. Whether a token was actually minted is carried on
    /// <see cref="EmailChangeRequestOutcome.Token"/> rather than a second branch here — see there for why.
    /// </summary>
    Requested,
}
