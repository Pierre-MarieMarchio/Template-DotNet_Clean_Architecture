using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.Model.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

public interface IGetStoredFilesUseCase : IUseCase<GetStoredFilesQuery, Result<PagedResult<StoredFileDto>>>;
