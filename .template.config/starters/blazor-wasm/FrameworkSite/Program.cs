using AppTemplate.Client;
//#if (localAuth)
using AppTemplate.Client.Common.Sessions;
//#endif
using AppTemplate.FrameworkSite;
//#if (externalAuth)
using AppTemplate.FrameworkSite.Common.Hosting;
//#endif
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
//#if (externalAuth)
using Microsoft.Extensions.Options;
//#endif

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//#if (localAuth)
builder.Services.AddSingleton<IRefreshTokenStore, CookieRefreshTokenStore>();
//#endif
//#if (externalAuth)
builder.Services.AddSignIn(builder.Configuration);
//#endif
builder.Services.AddApiClient(new Uri(builder.HostEnvironment.BaseAddress));

//#if (externalAuth)
var host = builder.Build();
host.Services.GetRequiredService<IStartupValidator>().Validate();
await host.RunAsync();
//#else
await builder.Build().RunAsync();
//#endif
