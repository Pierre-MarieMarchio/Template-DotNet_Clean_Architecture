using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Model.Entities;

namespace AppTemplate.Application.Features.Files.Services;

public interface IStoredFileDepositService
{
    Task<Result> ConfirmAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
}
