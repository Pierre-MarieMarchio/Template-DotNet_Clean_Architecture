using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests;

/// <summary>
/// Replacement, not addition. The module removes what the real modules registered before re-adding
/// its own doubles, and the difference only shows up under a resolution that does not pick the last
/// registration — <c>GetServices</c>, a container with a different tie-break, a future
/// <c>TryAdd</c>. A double that won by being registered last is a silent dependency on composition
/// order whose failure mode is a test quietly talking to a real SMTP relay.
/// <para>
/// The real modules are stood in for by substitutes. What is under test is that a prior registration
/// is displaced, and any prior registration demonstrates that.
/// </para>
/// </summary>
public sealed class InMemoryModuleTests
{
    [Fact]
    public void AddInMemoryModule_ReplacesAClockThatWasAlreadyRegistered()
    {
        var real = Substitute.For<IDateTimeProvider>();

        using var provider = ComposeAfter(services => services.AddSingleton(real));

        var clock = provider.GetRequiredService<IDateTimeProvider>();
        clock.ShouldBeOfType<FixedDateTimeProvider>();
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

    /// <summary>
    /// One registration per contract afterwards, however many there were before. Counting is the only
    /// way to tell removal from a double that merely happens to be resolved first.
    /// </summary>
    [Fact]
    public void AddInMemoryModule_LeavesOneRegistrationPerContractHoweverManyThereWere()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddSingleton(Substitute.For<IDateTimeProvider>());

        services.AddInMemoryModule();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        provider.GetServices<IDateTimeProvider>().ShouldHaveSingleItem().ShouldBeOfType<FixedDateTimeProvider>();
        scope.ServiceProvider.GetServices<IEmailSender>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The clock is registered under two contracts and has to be one object behind both: production
    /// code resolves the port, the test moves the concrete type, and a second instance would leave the
    /// test moving a clock nothing else reads.
    /// </summary>
    [Fact]
    public void AddInMemoryModule_ServesOneClockForEveryContractItIsRegisteredUnder()
    {
        using var provider = InMemoryHost.Compose();

        var port = provider.GetRequiredService<IDateTimeProvider>();
        var controllable = provider.GetRequiredService<FixedDateTimeProvider>();

        port.ShouldBeSameAs(controllable);
        controllable.Advance(TimeSpan.FromHours(1));
        port.UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant.AddHours(1));
    }

    /// <summary>
    /// The sender is per scope and the mailbox is not, so mail sent from several requests accumulates
    /// in one place — which is what an assertion made after those requests depends on.
    /// </summary>
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

    /// <summary>
    /// Composed first, it replaces nothing and is then replaced itself. This is why the host documents
    /// the call order, and pinning it here is what stops the order from being rearranged as cosmetic.
    /// </summary>
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
    public void AddInMemoryModule_ThrowsWhenThereIsNoContainerToComposeInto()
    {
        Should.Throw<ArgumentNullException>(() => InMemoryModule.AddInMemoryModule(services: null!));
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
