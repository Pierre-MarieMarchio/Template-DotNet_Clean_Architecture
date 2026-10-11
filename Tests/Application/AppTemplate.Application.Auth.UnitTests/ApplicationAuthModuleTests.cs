using AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.Auth.Ports.Sessions;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.AddRole;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LockAccount;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemoveRole;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RevokeSession;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.UnlockAccount;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests;

public sealed class ApplicationAuthModuleTests
{
    /// <summary>
    /// Twenty-four account and sign-in operations, one for signing in through an external identity
    /// provider, two security-trail reads, two administrative purges reached only from a background
    /// job, and the two the session list gives its owner: reading it and ending one of its entries.
    /// </summary>
    private const int _knownUseCaseCount = 31;

    public static TheoryData<Type> UseCaseImplementations =>
        [.. UseCaseDiscovery.Implementations];

    [Fact]
    public void TheDiscovery_FindsEveryUseCaseTheProjectDeclares() =>
        UseCaseDiscovery.Implementations.Count.ShouldBe(
            _knownUseCaseCount,
            "A use case was added or removed without this count following it. Discovery is what puts " +
            "it in the container, so the count is the only place the number is stated at all.");

    /// <summary>
    /// Each use case resolves through its own interface and nothing else: a container that binds the
    /// concrete class would let a controller depend on the implementation.
    /// </summary>
    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void EveryUseCase_ResolvesThroughItsOwnInterface(Type implementation)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService(UseCaseDiscovery.ContractOf(implementation))
            .ShouldBeOfType(implementation);
    }

    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void EveryUseCase_IsScopedAndBoundExactlyOnce(Type implementation)
    {
        var services = new ServiceCollection();
        services.AddAuthApplication();

        services.Single(descriptor => descriptor.ServiceType == UseCaseDiscovery.ContractOf(implementation))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void NoUseCase_IsBoundToItsConcreteType(Type implementation)
    {
        var services = new ServiceCollection();
        services.AddAuthApplication();

        services.ShouldNotContain(descriptor => descriptor.ServiceType == implementation);
    }

    /// <summary>
    /// The validators are discovered, not listed, so a command whose validator was never
    /// written would fail to resolve here rather than silently skip validation.
    /// </summary>
    [Theory]
    [InlineData(typeof(IValidator<RegisterCommand>))]
    [InlineData(typeof(IValidator<LoginCommand>))]
    [InlineData(typeof(IValidator<RefreshAccessTokenCommand>))]
    [InlineData(typeof(IValidator<ConfirmEmailCommand>))]
    [InlineData(typeof(IValidator<ResendConfirmationEmailCommand>))]
    [InlineData(typeof(IValidator<LogoutCommand>))]
    [InlineData(typeof(IValidator<ChangePasswordCommand>))]
    [InlineData(typeof(IValidator<RequestPasswordResetCommand>))]
    [InlineData(typeof(IValidator<ResetPasswordCommand>))]
    [InlineData(typeof(IValidator<RequestEmailChangeCommand>))]
    [InlineData(typeof(IValidator<ConfirmEmailChangeCommand>))]
    [InlineData(typeof(IValidator<AddRoleCommand>))]
    [InlineData(typeof(IValidator<RemoveRoleCommand>))]
    [InlineData(typeof(IValidator<LockAccountCommand>))]
    [InlineData(typeof(IValidator<UnlockAccountCommand>))]
    [InlineData(typeof(IValidator<DeleteAccountCommand>))]
    [InlineData(typeof(IValidator<ConfirmTwoFactorSetupCommand>))]
    [InlineData(typeof(IValidator<DisableTwoFactorCommand>))]
    [InlineData(typeof(IValidator<VerifyTwoFactorCommand>))]
    [InlineData(typeof(IValidator<SignInWithExternalProviderCommand>))]
    [InlineData(typeof(IValidator<DeleteMyAccountCommand>))]
    [InlineData(typeof(IValidator<RevokeSessionCommand>))]
    public void EveryValidator_IsDiscovered(Type validatorType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(validatorType).ShouldNotBeNull();
    }

    /// <summary>
    /// Each <c>IValidator&lt;T&gt;</c> is bound exactly once. A second registration mechanism would
    /// leave "last one wins" deciding which instance a use case received.
    /// </summary>
    [Theory]
    [InlineData(typeof(IValidator<RegisterCommand>))]
    [InlineData(typeof(IValidator<LoginCommand>))]
    [InlineData(typeof(IValidator<LogoutCommand>))]
    public void EachValidator_IsRegisteredExactlyOnce(Type validatorType)
    {
        var services = new ServiceCollection();
        services.AddAuthApplication();

        services.Count(descriptor => descriptor.ServiceType == validatorType).ShouldBe(1);
    }

    [Fact]
    public void TheEntryPoint_Rejects_ANullServiceCollection() =>
        Should.Throw<ArgumentNullException>(() => ApplicationAuthModule.AddAuthApplication(null!));

    /// <summary>
    /// Nothing in this project reads settings, so the entry point takes no <c>IConfiguration</c> —
    /// asking for configuration it does not use would invite the infrastructure knowledge the layer
    /// exists to avoid.
    /// </summary>
    [Fact]
    public void TheEntryPoint_AsksForNothingButTheServiceCollection() =>
        typeof(ApplicationAuthModule)
            .GetMethod(nameof(ApplicationAuthModule.AddAuthApplication))!
            .GetParameters().Length.ShouldBe(1);

    /// <summary>
    /// Scope validation plus eager building means a missing dependency fails here rather than at
    /// the first request.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddAuthApplication();

        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddScoped(_ => Substitute.For<IDateTimeProvider>());
        services.AddScoped(_ => Substitute.For<IEmailSender>());
        services.AddScoped(_ => Substitute.For<IAccountsService>());
        services.AddScoped(_ => Substitute.For<IExternalIdentityVerifier>());
        services.AddScoped(_ => Substitute.For<IExternalLoginsService>());
        services.AddScoped(_ => Substitute.For<IEmailTokensService>());
        services.AddScoped(_ => Substitute.For<IAccountEmailFactory>());
        services.AddScoped(_ => Substitute.For<ISessionTokensService>());
        services.AddScoped(_ => Substitute.For<ISessionsService>());
        services.AddScoped(_ => Substitute.For<ICurrentSession>());
        services.AddScoped(_ => Substitute.For<ISecurityEventLog>());
        services.AddScoped(_ => Substitute.For<ISecurityEventQueries>());
        services.AddScoped(_ => Substitute.For<IAccountAdministrationService>());
        services.AddScoped(_ => Substitute.For<ITwoFactorService>());
        services.AddScoped(_ => Substitute.For<IReAuthenticationService>());
        services.AddScoped(_ => Substitute.For<ICurrentAccessToken>());
        services.AddScoped(_ => Substitute.For<IDomainEventPublisher>());

        // Several use cases take an ILogger, which every real host supplies.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
