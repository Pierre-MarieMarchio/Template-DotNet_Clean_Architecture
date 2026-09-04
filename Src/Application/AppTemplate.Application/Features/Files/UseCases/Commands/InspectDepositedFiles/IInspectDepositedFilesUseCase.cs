using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>
/// One pass of the content inspection.
/// </summary>
/// <returns>How many files reached a verdict — released or refused. Files that could not be
/// examined are not counted, because a pass that examined nothing and a pass that had nothing to
/// examine must not report the same number.</returns>
public interface IInspectDepositedFilesUseCase : IUseCase<Result<int>>;
