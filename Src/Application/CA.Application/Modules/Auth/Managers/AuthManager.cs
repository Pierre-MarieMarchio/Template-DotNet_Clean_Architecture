using CA.Application.Modules.Auth.Interfaces.Managers;
using CA.Application.Modules.Auth.Interfaces.UseCases;

namespace CA.Application.Modules.Auth.Managers;


public class AuthManager : IAuthManager
{
    public IRegisterUseCase Register { get; }
    public ILoginUseCase Login { get; }
    public IAuthenticateUseCase Authenticate { get; }
    public IConfirmEmailUseCase ValidateEmail { get; }

    public AuthManager(
        IRegisterUseCase register,
        ILoginUseCase login,
        IAuthenticateUseCase authenticate,
        IConfirmEmailUseCase validateEmail
        )
    {
        Register = register;
        Login = login;
        Authenticate = authenticate;
        ValidateEmail = validateEmail;
    }
}
