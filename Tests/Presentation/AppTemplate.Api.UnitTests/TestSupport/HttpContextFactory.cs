using AppTemplate.Api.Core.Common.Concurrency;
using AppTemplate.Api.Core.Common.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.UnitTests.TestSupport;

/// <summary>
/// A minimal <see cref="HttpContext"/> wired with just enough <c>RequestServices</c> for the code
/// under test — <c>ProblemDetailsNormaliser.Normalise</c> resolves
/// <see cref="IOptions{TOptions}"/> of <see cref="ProblemTypeOptions"/>, and
/// <c>ApiControllerBase.ReadPrecondition</c> resolves <see cref="IOptions{TOptions}"/> of
/// <see cref="ConcurrencyOptions"/> — without starting a real host for either.
/// </summary>
internal static class HttpContextFactory
{
    public static DefaultHttpContext Create(
        string? problemTypeBaseUri = null,
        IfMatchRequirement ifMatchRequirement = IfMatchRequirement.Optional)
    {
        var services = new ServiceCollection();

        var problemTypes = new ProblemTypeOptions();

        if (problemTypeBaseUri is not null)
        {
            problemTypes.BaseUri = problemTypeBaseUri;
        }

        services.AddSingleton(Options.Create(problemTypes));

        services.AddSingleton(Options.Create(new ConcurrencyOptions { IfMatch = ifMatchRequirement }));

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }
}
