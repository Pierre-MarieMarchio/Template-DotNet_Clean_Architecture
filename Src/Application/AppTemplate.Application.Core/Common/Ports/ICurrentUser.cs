namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Who the request is on behalf of, and nothing about how they proved it: the application layer has
/// no business knowing which scheme the host authenticated with.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The caller's id, or <c>null</c> when the request is anonymous.</summary>
    Guid? UserId { get; }
}
