using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Files.Ports.FileContentInventory;

public interface IFileContentInventory
{
    Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default);
}
