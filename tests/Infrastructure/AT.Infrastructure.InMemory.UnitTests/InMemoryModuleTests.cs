//#if (localAuth)
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
//#endif
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.InMemory.Common.Email;
//#if (localAuth)
using AppTemplate.Infrastructure.InMemory.Features.ExternalLogins;
//#endif
using AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests;

public sealed class InMemoryModuleTests
{
    [Fact]
    public void AddInMemoryModule_ReplacesAClockThatWasAlreadyRegistered()
    {
        var real = new FakeTimeProvider();

        using var provider = ComposeAfter(services => services.AddSingleton(real));

        var clock = provider.GetRequiredService<TimeProvider>();
        clock.ShouldBeOfType<FakeTimeProvider>();
        clock.ShouldNotBeSameAs(real);
    }

    [Fact]
    public async Task AddInMemoryModule_ReplacesAnEmailSenderThatWasAlreadyRegistered()
    {
        var real = Substitute.For<IEmailSender>();

        using var provider = ComposeAfter(services => services.AddSingleton(real));

        await InMemoryHost.SendAsync(
            provider,
            "someone@example.invalid",
            "Subject",
            "<p>Body</p>",
            TestContext.Current.CancellationToken);

        InMemoryHost.MailboxOf(provider).Snapshot().ShouldHaveSingleItem();
        await real.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
    //#if (localAuth)

    [Fact]
    public void AddInMemoryModule_ReplacesAnExternalIdentityVerifierThatWasAlreadyRegistered()
    {
        var real = Substitute.For<IExternalIdentityVerifier>();

        using var provider = ComposeAfter(services => services.AddSingleton(real));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IExternalIdentityVerifier>()
            .ShouldHaveSingleItem()
            .ShouldNotBeSameAs(real);
    }

    [Fact]
    public void AddInMemoryModule_ServesOneSetOfArrangedIdentitiesForEveryScope()
    {
        using var provider = InMemoryHost.Compose();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<AcceptedExternalIdentities>()
            .ShouldBeSameAs(provider.GetRequiredService<AcceptedExternalIdentities>());
    }
    //#endif

    [Fact]
    public void AddInMemoryModule_LeavesOneRegistrationPerContractHoweverManyThereWere()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(new FakeTimeProvider());
        services.AddSingleton(new FakeTimeProvider());

        services.AddInMemoryModule();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        provider.GetServices<TimeProvider>().ShouldHaveSingleItem().ShouldBeOfType<FakeTimeProvider>();
        scope.ServiceProvider.GetServices<IEmailSender>().ShouldHaveSingleItem();
    }

    [Fact]
    public void AddInMemoryModule_ServesOneClockForEveryContractItIsRegisteredUnder()
    {
        using var provider = InMemoryHost.Compose();

        var port = provider.GetRequiredService<TimeProvider>();
        var controllable = provider.GetRequiredService<FakeTimeProvider>();

        port.ShouldBeSameAs(controllable);
        controllable.Advance(TimeSpan.FromHours(1));
        port.GetUtcNow().ShouldBe(InMemoryModule.ClockStart.AddHours(1));
    }

    [Fact]
    public async Task AddInMemoryModule_ServesOneMailboxForEveryScope()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(provider, "first@example.invalid", "First", "<p>1</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, "second@example.invalid", "Second", "<p>2</p>", TestContext.Current.CancellationToken);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<RecordedEmails>()
            .ShouldBeSameAs(InMemoryHost.MailboxOf(provider));
        InMemoryHost.MailboxOf(provider).Snapshot().Count.ShouldBe(2);
    }

    [Fact]
    public void AddInMemoryModule_ReplacesNothingWhenComposedBeforeTheRealModules()
    {
        var real = Substitute.For<IEmailSender>();
        var services = new ServiceCollection();

        services.AddInMemoryModule();
        services.AddSingleton(real);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEmailSender>().ShouldBeSameAs(real);
    }

    [Fact]
    public void AddInMemoryModule_ReturnsTheCollectionItWasGiven()
    {
        var services = new ServiceCollection();

        services.AddInMemoryModule().ShouldBeSameAs(services);
    }

    private static ServiceProvider ComposeAfter(Action<IServiceCollection> realModules)
    {
        var services = new ServiceCollection();
        realModules(services);

        return services.AddInMemoryModule().BuildServiceProvider(validateScopes: true);
    }
}
