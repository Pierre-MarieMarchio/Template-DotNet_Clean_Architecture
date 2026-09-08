using AppTemplate.Application.Auth.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Auth.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenMaintenance;
using AppTemplate.Application.Auth.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.AddRole;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;
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
    /// Twenty-three account and sign-in operations, one for signing in through an external identity
    /// provider, and one administrative purge reached only from the worker.
    /// </summary>
    private const int _knownUseCaseCount = 25;

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
        services.AddScoped(_ => Substitute.For<IUserAccountsService>());
        services.AddScoped(_ => Substitute.For<IUserProfilesService>());
        services.AddScoped(_ => Substitute.For<IExternalIdentityVerifier>());
        services.AddScoped(_ => Substitute.For<IExternalLoginsService>());
        services.AddScoped(_ => Substitute.For<IEmailConfirmationTokensService>());
        services.AddScoped(_ => Substitute.For<IPasswordResetTokensService>());
        services.AddScoped(_ => Substitute.For<IPasswordResetEmailFactory>());
        services.AddScoped(_ => Substitute.For<IAccessTokenIssuer>());
        services.AddScoped(_ => Substitute.For<IRefreshTokenGrantsService>());
        services.AddScoped(_ => Substitute.For<IConfirmationEmailFactory>());
        services.AddScoped(_ => Substitute.For<IRefreshTokenMaintenanceService>());
        services.AddScoped(_ => Substitute.For<ISecurityEventLog>());
        services.AddScoped(_ => Substitute.For<IEmailChangeTokensService>());
        services.AddScoped(_ => Substitute.For<IEmailChangeEmailFactory>());
        services.AddScoped(_ => Substitute.For<IAccountLockoutsService>());
        services.AddScoped(_ => Substitute.For<IRoleAssignmentsService>());
        services.AddScoped(_ => Substitute.For<IAccountDeletionService>());
        services.AddScoped(_ => Substitute.For<ITwoFactorEnrollmentService>());
        services.AddScoped(_ => Substitute.For<ITwoFactorChallengeService>());
        services.AddScoped(_ => Substitute.For<ITwoFactorAdministrationService>());

        // Several use cases take an ILogger, which every real host supplies.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
