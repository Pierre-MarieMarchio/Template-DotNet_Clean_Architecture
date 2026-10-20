using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

public interface IInspectDepositedFilesUseCase : IUseCase<Result<int>>;
