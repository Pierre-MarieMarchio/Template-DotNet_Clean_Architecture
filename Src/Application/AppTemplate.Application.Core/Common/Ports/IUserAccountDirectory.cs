using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

public interface IUserAccountDirectory
{
    Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default);
}
