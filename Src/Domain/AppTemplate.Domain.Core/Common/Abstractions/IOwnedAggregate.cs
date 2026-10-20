using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Core.Common.Abstractions;

public interface IOwnedAggregate
{
    UserId OwnerId { get; }
}
