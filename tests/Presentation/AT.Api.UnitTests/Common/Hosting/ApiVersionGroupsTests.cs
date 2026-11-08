using System.Reflection;
using AppTemplate.Api.Common.Hosting;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

public sealed class ApiVersionGroupsTests
{
    [Fact]
    public async Task TheApisVersionGroups_AreTheOnesTheVersionedApiExplorerDescribes()
    {
        var (groups, described) = await GroupsOf(parts => parts.ApplicationParts.Add(new AssemblyPart(typeof(HttpPipelineModule).Assembly)));

        described.ShouldNotBeEmpty("The API's controllers describe no version, so this comparison holds by comparing nothing.");
        groups.ShouldBe(described, ignoreOrder: true);
    }

    [Fact]
    public async Task EveryWayOfDeclaringAVersion_YieldsTheGroupsTheVersionedApiExplorerDescribes()
    {
        var (groups, described) = await GroupsOf(parts => parts.FeatureProviders.Add(new DeclaredControllers(
            typeof(TwoVersionsController),
            typeof(MappedActionController),
            typeof(AdvertisingController))));

        described.ShouldBe(["v1", "v2", "v3"], ignoreOrder: true);
        groups.ShouldBe(described, ignoreOrder: true);
    }

    [Theory]
    [InlineData(typeof(UnversionedApiController), new[] { "v1", "v2" })]
    [InlineData(typeof(VersionNeutralController), new[] { "v2" })]
    [InlineData(typeof(NotAnApiController), new[] { "v2" })]
    public async Task AControllerDeclaringNoVersion_JoinsTheGroupsTheVersionedApiExplorerGivesIt(Type controller, string[] expected)
    {
        var (groups, described) = await GroupsOf(parts => parts.FeatureProviders.Add(new DeclaredControllers(
            typeof(SecondVersionController),
            controller)));

        described.ShouldBe(expected, ignoreOrder: true);
        groups.ShouldBe(described, ignoreOrder: true);
    }

    [Fact]
    public async Task AnApiWithNoControllerYet_KeepsTheDefaultGroupTheVersionedApiExplorerDescribes()
    {
        var (groups, described) = await GroupsOf(_ => { });

        described.ShouldBe(["v1"]);
        groups.ShouldBe(described);
    }

    private static async Task<(IReadOnlyList<string> Groups, string[] Described)> GroupsOf(Action<ApplicationPartManager> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var controllers = builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            configure(parts);
        });
        builder.Services.AddUrlSegmentApiVersioning();

        await using var app = builder.Build();

        string[] described =
        [
            .. app.Services.GetRequiredService<IApiVersionDescriptionProvider>()
                .ApiVersionDescriptions
                .Select(description => description.GroupName),
        ];

        return (controllers.ApiVersionGroups(), described);
    }

    private sealed class DeclaredControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
            {
                feature.Controllers.Add(controller.GetTypeInfo());
            }
        }
    }

    [ApiController]
    [ApiVersion("1.0")]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/two-versions")]
    public sealed class TwoVersionsController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }

    [ApiController]
    [ApiVersion("2.0")]
    [ApiVersion("3.0")]
    [Route("api/v{version:apiVersion}/mapped")]
    public sealed class MappedActionController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();

        [HttpPost]
        [MapToApiVersion("3.0")]
        public IActionResult Write() => Ok();
    }

    [ApiController]
    [ApiVersion("1.0")]
    [AdvertiseApiVersions("4.0")]
    [Route("api/v{version:apiVersion}/advertising")]
    public sealed class AdvertisingController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }

    [ApiController]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/second-version")]
    public sealed class SecondVersionController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }

    [ApiController]
    [Route("api/v{version:apiVersion}/unversioned")]
    public sealed class UnversionedApiController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }

    [ApiController]
    [ApiVersionNeutral]
    [Route("api/neutral")]
    public sealed class VersionNeutralController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }

    [Route("not-an-api")]
    public sealed class NotAnApiController : ControllerBase
    {
        [HttpGet]
        public IActionResult Read() => Ok();
    }
}
