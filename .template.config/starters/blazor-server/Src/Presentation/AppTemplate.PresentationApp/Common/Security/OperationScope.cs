using Microsoft.AspNetCore.Components.Authorization;

namespace AppTemplate.PresentationApp.Common.Security;

public sealed class OperationScope(IServiceScopeFactory scopes, AuthenticationStateProvider authentication)
{
    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> operation)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        var state = await authentication.GetAuthenticationStateAsync();

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CallerPrincipal>().Principal = state.User;

        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
