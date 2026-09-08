using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;

/// <summary>
/// The tags this caller has already used on their files, for a picker or a filter. The owner comes
/// from <see cref="ICurrentUser"/> and there is nothing else to ask for, so the operation takes no
/// request.
/// </summary>
public interface IGetUsedFileTagsUseCase : IUseCase<Result<IReadOnlyList<string>>>;
