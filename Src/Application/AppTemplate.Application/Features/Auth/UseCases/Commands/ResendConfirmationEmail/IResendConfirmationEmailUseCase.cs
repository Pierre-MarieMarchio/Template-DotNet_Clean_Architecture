using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

/// <summary>
/// Exists because sign-up commits the account before handing the mail to the relay: without a
/// resend path, a delivery failure would take the address forever.
/// </summary>
public interface IResendConfirmationEmailUseCase : IUseCase<ResendConfirmationEmailCommand, Result>;
