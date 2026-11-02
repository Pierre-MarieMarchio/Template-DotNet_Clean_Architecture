//#if (localAuth)
using System.Collections.Concurrent;
using System.Xml.Linq;
//#endif
using AppTemplate.Application.Core.Common.UseCases;
//#if (localAuth)
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
//#endif
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace AppTemplate.PresentationAppMcp.UnitTests.TestSupport;

internal sealed class McpHost(Action<IServiceCollection>? replace = null) : WebApplicationFactory<Program>
{
    internal const string Resource = "http://localhost/mcp";

    private readonly List<Type> _useCases = [];

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
            ["Files:Storage:S3:BucketName"] = "mcp-tests",
            //#endif
            //#if (externalAuth)
            ["Authentication:External:Authority"] = TestTokens.Issuer,
            ["Authentication:External:Audience"] = TestTokens.ApiAudience,
            ["Mcp:Resource"] = Resource,
            //#endif
        };

    internal async Task<McpClient> ConnectAsync(string bearer, CancellationToken cancellationToken)
    {
        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", bearer);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

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

            //#if (localAuth)
            TestKeys.Trust(services);

            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryKeyRing());
            //#endif
            //#if (externalAuth)
            TestTokens.Trust(services);
            //#endif
            replace?.Invoke(services);
        });
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
