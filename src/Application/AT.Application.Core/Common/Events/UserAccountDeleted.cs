using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Events;

public sealed record UserAccountDeleted(UserId UserId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public DateTimeOffset OccurredOn => OccurredAt;
}
