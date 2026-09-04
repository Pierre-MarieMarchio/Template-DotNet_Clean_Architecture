using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestPasswordReset;

/// <summary>
/// Without this, an account whose password was forgotten is locked out for good and its address
/// stays taken, the email index being unique. Answers in success for every address — known, unknown
/// or unconfirmed — the same anti-enumeration pattern <c>ResendConfirmationEmailUseCase</c> uses.
/// </summary>
public interface IRequestPasswordResetUseCase : IUseCase<RequestPasswordResetCommand, Result>;
