using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Auth.Policies;

/// <summary>
/// What every administrative use case that can strip its own caller's access owes: refusing to
/// target the caller itself.
/// <para>
/// Locking an account and removing a role both rotate the security stamp, which invalidates the
/// access token the request making the call is carrying. An administrator who could target
/// themselves this way could end the one session capable of undoing it — an accidental,
/// self-inflicted lockout with nobody else logged in to reverse it. Deleting an account is the same
/// problem with nothing left afterwards to undo anything at all.
/// </para>
/// </summary>
public static class SelfAdministrationPolicy
{
    /// <param name="error">
    /// Failed with as given, so each use case can name its own operation rather than share one
    /// message that fits none of them exactly.
    /// </param>
    /// <returns>A failure when the two ids are the same, success otherwise.</returns>
    public static Result EnsureNotSelf(Guid callerId, Guid targetId, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return callerId == targetId ? Result.Failure(error) : Result.Success();
    }
}
