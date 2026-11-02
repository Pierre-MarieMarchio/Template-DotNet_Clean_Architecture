using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public interface ICreateAccessKeyUseCase : IUseCase<CreateAccessKeyCommand, Result<CreateAccessKeyOutcome>>;
