using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

public interface ISweepDeletedAccountDataUseCase : IUseCase<Result<int>>
{
}
