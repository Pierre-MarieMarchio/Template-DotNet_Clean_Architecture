//#if (localAuth)
using AppTemplate.Client.Common.Errors;
using AppTemplate.Client.Common.Sessions;
using AppTemplate.Client.Generated;
using AppTemplate.Client.Generated.Models;
//#endif
using AppTemplate.PresentationAppDesktop.Core.Common.Hosting;
//#if (externalAuth)
using AppTemplate.PresentationAppDesktop.Core.Common.Sessions;
//#endif
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
//#if (localAuth)
using Microsoft.Kiota.Abstractions;
//#endif

namespace AppTemplate.PresentationAppDesktop.Core.ViewModels;

//#if (localAuth)
public partial class MainViewModel(ApiClient api, ApiSession session, IOptions<ApiOptions> options) : ObservableObject
{
    public const string SecondFactorRequired = "This account signs in with a second factor, which this app does not ask for.";

    public const string ApiUnreachable = "The API does not answer.";

    public string ApiAddress => options.Value.BaseAddress?.ToString() ?? string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SignedInAs { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Problem = null;

        try
        {
            var answer = await api.Api.V1.Auth.Login.PostAsync(
                new LoginRequest { Email = Email, Password = Password },
                cancellationToken: cancellationToken);

            if (answer?.LoginResponseAuthenticated?.Tokens is not { } tokens)
            {
                Problem = SecondFactorRequired;
                return;
            }

            await session.SignedInAsync(tokens, cancellationToken);
            var me = await api.Api.V1.Auth.Me.GetAsync(cancellationToken: cancellationToken);

            Password = string.Empty;
            SignedInAs = me?.Email ?? Email;
        }
        catch (ApiException error)
        {
            Problem = ApiProblem.From(error).Code;
        }
        catch (HttpRequestException)
        {
            Problem = ApiUnreachable;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await session.SignOutAsync(cancellationToken);
        SignedInAs = null;
    }
}
//#else
public partial class MainViewModel(DesktopSession session, IOptions<ApiOptions> options) : ObservableObject
{
    public const string FinishInTheBrowser = "Finish signing in in your browser.";

    public string ApiAddress => options.Value.BaseAddress?.ToString() ?? string.Empty;

    [ObservableProperty]
    public partial string? SignedInAs { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Problem = null;
        Status = FinishInTheBrowser;

        try
        {
            await session.SignInAsync(cancellationToken);
            SignedInAs = session.SignedInAs;
        }
        catch (SignInFailedException error)
        {
            Problem = error.Message;
        }
        finally
        {
            Status = null;
        }
    }

    [RelayCommand]
    private void SignOut()
    {
        session.SignOut();
        SignedInAs = null;
    }
}
//#endif
