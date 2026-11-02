//#if (localAuth)
using System.Collections.Concurrent;
//#endif
using System.Security.Claims;
//#if (localAuth)
using System.Xml.Linq;
//#endif
using Microsoft.AspNetCore.Components.Authorization;
//#if (localAuth)
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
//#endif
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mediator;

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
                .Where(type => type.IsGenericType
                    && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                        || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
                .Distinct());

            services.RemoveAll<AuthenticationStateProvider>();
            services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(_caller));
            //#if (localAuth)

            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryKeyRing());
            //#endif
        });
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal caller) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(caller));
    }
    //#if (localAuth)

    private sealed class InMemoryKeyRing : IXmlRepository
    {
        private readonly ConcurrentQueue<XElement> _elements = new();

        public IReadOnlyCollection<XElement> GetAllElements() => [.. _elements];

        public void StoreElement(XElement element, string friendlyName) => _elements.Enqueue(element);
    }
    //#endif
}
