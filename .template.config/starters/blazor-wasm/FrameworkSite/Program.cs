using AppTemplate.Client;
//#if (localAuth || externalAuth)
using AppTemplate.Client.Common.Sessions;
//#endif
using AppTemplate.FrameworkSite;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//#if (localAuth)
builder.Services.AddSingleton<IRefreshTokenStore, CookieRefreshTokenStore>();
//#endif
//#if (externalAuth)
builder.Services.AddSingleton<IAccessTokenSource, NoAccessTokenYet>();
//#endif
builder.Services.AddApiClient(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();

//#if (externalAuth)
internal sealed class NoAccessTokenYet : IAccessTokenSource
{
    public ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(null);
}
//#endif
