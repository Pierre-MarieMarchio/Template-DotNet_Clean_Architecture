using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

/// <summary>Self-service: the caller ends one of their own sessions, never somebody else's.</summary>
public interface IRevokeSessionUseCase : IUseCase<RevokeSessionCommand, Result>;
