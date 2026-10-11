using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemovePasskey;

/// <summary>Self-service: the caller removes one of their own passkeys, never somebody else's.</summary>
public interface IRemovePasskeyUseCase : IUseCase<RemovePasskeyCommand, Result>;
