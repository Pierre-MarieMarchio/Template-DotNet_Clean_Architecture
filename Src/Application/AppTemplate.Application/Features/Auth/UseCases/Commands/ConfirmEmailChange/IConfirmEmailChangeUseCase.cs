using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>Authenticated. See <see cref="ConfirmEmailChangeCommand"/>.</summary>
public interface IConfirmEmailChangeUseCase : IUseCase<ConfirmEmailChangeCommand, Result>;
