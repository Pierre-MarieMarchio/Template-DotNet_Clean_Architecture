using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// The owner filter comes from <see cref="ICurrentUser"/> and is deliberately not part of the
/// query, so no caller can widen it.
/// </summary>
public interface IGetStoredFilesUseCase : IUseCase<GetStoredFilesQuery, Result<PagedResult<StoredFileDto>>>;
