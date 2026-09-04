namespace AppTemplate.Application.Common.Abstractions;

public interface ICurrentUser
{
    /// <summary>The caller's id, or <c>null</c> when the request is anonymous.</summary>
    Guid? UserId { get; }
}
