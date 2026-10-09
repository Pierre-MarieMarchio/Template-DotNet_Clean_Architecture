using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>One pass of the content inspection.</summary>
/// <returns>How many files reached a verdict, released or refused; deferred files are not counted.</returns>
public interface IInspectDepositedFilesUseCase : IUseCase<Result<int>>;
