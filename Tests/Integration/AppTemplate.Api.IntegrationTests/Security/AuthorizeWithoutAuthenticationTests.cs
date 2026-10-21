/*#if (publicApi)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class AuthorizeWithoutAuthenticationTests
{
    [Fact]
    public void AnAuthorizeAttribute_RefusesTheStartOfAnApiThatAuthenticatesNobody()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services
                .AddControllers()
                .AddApplicationPart(typeof(GuardedController).Assembly)));

        var refusal = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        refusal.Message.ShouldContain(nameof(GuardedController));
    }
}

[ApiController]
[Route("guarded")]
[Authorize]
public sealed class GuardedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
#endif*/
