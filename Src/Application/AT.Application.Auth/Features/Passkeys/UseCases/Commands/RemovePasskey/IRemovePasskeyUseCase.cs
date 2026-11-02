using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

public interface IRemovePasskeyUseCase : IUseCase<RemovePasskeyCommand, Result>;
