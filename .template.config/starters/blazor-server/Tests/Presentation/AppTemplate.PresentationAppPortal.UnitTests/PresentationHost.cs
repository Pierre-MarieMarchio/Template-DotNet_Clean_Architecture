using System.Security.Claims;
using AppTemplate.Application.Core.Common.UseCases;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.PresentationAppPortal.UnitTests;

internal sealed class PresentationHost : WebApplicationFactory<Program>
{
    private readonly ClaimsPrincipal _caller;

    private readonly List<Type> _useCases = [];

    internal PresentationHost(ClaimsPrincipal? caller = null)
    {
        _caller = caller ?? new ClaimsPrincipal(new ClaimsIdentity());
    }

    internal IReadOnlyList<Type> UseCases
    {
        get
        {
            _ = Services;

            return _useCases;
        }
    }

    internal static IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            //#if (files)
            ["Files:Storage:S3:BucketName"] = "presentation-tests",
            //#endif
            //#if (externalAuth)
            ["Authentication:External:Authority"] = "https://idp.example.com/realms/app",
            ["Authentication:External:Audience"] = "app-api",
            ["Authentication:External:Interactive:ClientId"] = "app-portal",
            //#endif
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        foreach ((string key, string? value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            _useCases.AddRange(services
                .Select(descriptor => descriptor.ServiceType)
                .Where(type => typeof(IUseCase).IsAssignableFrom(type))
                .Distinct());

            services.RemoveAll<AuthenticationStateProvider>();
            services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(_caller));
        });
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal caller) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(caller));
    }
}
