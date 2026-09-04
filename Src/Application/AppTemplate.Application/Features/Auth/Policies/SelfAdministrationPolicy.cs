using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.Policies;

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
    public static Result EnsureNotSelf(Guid callerId, Guid targetId, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return callerId == targetId ? Result.Failure(error) : Result.Success();
    }
}
