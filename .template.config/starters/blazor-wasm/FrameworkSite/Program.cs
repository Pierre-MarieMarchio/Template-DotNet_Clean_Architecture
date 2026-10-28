using AppTemplate.Client;
//#if (localAuth)
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
builder.Services.AddApiClient(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
