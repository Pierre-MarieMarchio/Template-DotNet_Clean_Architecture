using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

/// <summary>
/// Removes the data of accounts that are gone, whether or not the deletion event reached the
/// feature that holds it. Administrative rather than user-facing: nothing about a caller's own
/// request needs this. Scheduled on the maintenance timer.
/// </summary>
public interface ISweepDeletedAccountDataUseCase : IUseCase<Result<int>>
{
}
