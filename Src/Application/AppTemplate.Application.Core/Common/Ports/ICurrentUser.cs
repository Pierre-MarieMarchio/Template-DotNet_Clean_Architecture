using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

public interface ICurrentUser
{
    UserId? UserId { get; }
}
