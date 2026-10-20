using System.Security.Claims;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class CallerIdentityTests
{
    private static readonly Guid _caller = Guid.CreateVersion7();

    [Fact]
    public void ARequestScope_ReportsTheAuthenticatedCaller_ToBothQuestions()
    {
        using var provider = Compose();
        SignIn(provider, _caller);

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId!.Value.ShouldBe(_caller);
        scope.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBe(_caller);
    }

    [Fact]
    public void AnAnonymousRequest_HasNoCaller_AndRecordsNobody_WithoutThrowing()
    {
        using var provider = Compose();
        SignIn(provider, null);

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.ShouldBeNull();
        scope.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ABackgroundScope_RefusesTheQuestionOfWhoIsCalling()
    {
        using var provider = Compose();

        await using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

        Should.Throw<NotSupportedException>(
            () => scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
    }

    [Fact]
    public async Task ABackgroundScope_RecordsNobody_BecauseNoRequestStandsBehindTheWrite()
    {
        using var provider = Compose();

        await using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

        scope.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ABackgroundScope_IgnoresWhateverRequestIsAmbient()
    {
        using var provider = Compose();
        SignIn(provider, _caller);

        await using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

        scope.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBeNull();
        Should.Throw<NotSupportedException>(
            () => scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
    }

    [Fact]
    public async Task ARequestScope_OpenedAfterABackgroundOne_StillSeesItsCaller()
    {
        using var provider = Compose();
        SignIn(provider, _caller);

        await using (var background = provider.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope())
        {
            background.ServiceProvider.GetRequiredService<BackgroundScope>().IsActive.ShouldBeTrue();
        }

        using var request = provider.CreateScope();

        request.ServiceProvider.GetRequiredService<BackgroundScope>().IsActive.ShouldBeFalse();
        request.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBe(_caller);
    }

    private static ServiceProvider Compose()
    {
        var services = new ServiceCollection();
        services.AddApiCallerIdentity();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void SignIn(ServiceProvider provider, Guid? userId)
    {
        var identity = userId is { } id
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test")
            : new ClaimsIdentity();

        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
