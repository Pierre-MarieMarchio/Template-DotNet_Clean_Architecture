using CA.Application.Modules.Auth.Interfaces.UseCases;

namespace CA.Application.Modules.Auth.Interfaces.Managers;

public interface IAuthManager
{
    IRegisterUseCase Register { get; }
    ILoginUseCase Login { get; }
    IAuthenticateUseCase Authenticate { get; }
    IConfirmEmailUseCase ValidateEmail { get; }

}

