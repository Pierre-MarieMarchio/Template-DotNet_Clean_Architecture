using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// In this host the two questions have the same answer: the row is stamped with whoever made the
/// request, and an anonymous request stamps nothing. It delegates rather than reading the principal
/// a second time, so there is one place that turns a claim into a user id.
/// </summary>
internal sealed class CurrentUserAuditActor(ICurrentUser currentUser) : IAuditActor
{
    public Guid? UserId => currentUser.UserId;
}
