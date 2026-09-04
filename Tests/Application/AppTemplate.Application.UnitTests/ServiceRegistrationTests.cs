using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using AppTemplate.Domain.Features.TodoLists.Stores;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests;

public sealed class ServiceRegistrationTests
{
    /// <summary>
    /// Fifteen to-do list operations, eleven authentication ones, and two maintenance operations.
    /// </summary>
    private const int _knownUseCaseCount = 28;

    public static TheoryData<Type> UseCaseImplementations =>
        [.. UseCaseDiscovery.Implementations];

    [Fact]
    public void TheDiscovery_FindsEveryUseCaseTheLayerDeclares() =>
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
        services.AddApplicationLayer();

        services.Single(descriptor => descriptor.ServiceType == UseCaseDiscovery.ContractOf(implementation))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void NoUseCase_IsBoundToItsConcreteType(Type implementation)
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        services.ShouldNotContain(descriptor => descriptor.ServiceType == implementation);
    }

    /// <summary>
    /// A use case that declares no interface of its own, or several, has no single service type to
    /// bind. Registration says so at start-up instead of choosing for the author.
    /// </summary>
    [Theory]
    [InlineData(typeof(UseCaseWithNoContract))]
    [InlineData(typeof(UseCaseWithTwoContracts))]
    public void ARegistrationWithoutOneNamedInterface_FailsAtStartUp(Type implementation) =>
        Should.Throw<InvalidOperationException>(
                () => new ServiceCollection().AddUseCases([implementation]))
            .Message.ShouldContain(implementation.FullName!);

    /// <summary>
    /// The validators are discovered, not listed, so a command whose validator was never
    /// written would fail to resolve here rather than silently skip validation.
    /// </summary>
    [Theory]
    [InlineData(typeof(IValidator<CreateTodoListCommand>))]
    [InlineData(typeof(IValidator<RenameTodoListCommand>))]
    [InlineData(typeof(IValidator<AddTodoItemCommand>))]
    [InlineData(typeof(IValidator<DeleteTodoListCommand>))]
    [InlineData(typeof(IValidator<CompleteTodoItemCommand>))]
    [InlineData(typeof(IValidator<RemoveTodoItemCommand>))]
    [InlineData(typeof(IValidator<UpdateTodoItemCommand>))]
    [InlineData(typeof(IValidator<ReopenTodoItemCommand>))]
    [InlineData(typeof(IValidator<AddTagToTodoItemCommand>))]
    [InlineData(typeof(IValidator<RemoveTagFromTodoItemCommand>))]
    [InlineData(typeof(IValidator<ReplaceTodoItemTagsCommand>))]
    [InlineData(typeof(IValidator<GetTodoListQuery>))]
    [InlineData(typeof(IValidator<GetTodoItemQuery>))]
    [InlineData(typeof(IValidator<GetTodoItemsQuery>))]
    [InlineData(typeof(IValidator<RegisterCommand>))]
    [InlineData(typeof(IValidator<LoginCommand>))]
    [InlineData(typeof(IValidator<RefreshAccessTokenCommand>))]
    [InlineData(typeof(IValidator<ConfirmEmailCommand>))]
    [InlineData(typeof(IValidator<ResendConfirmationEmailCommand>))]
    [InlineData(typeof(IValidator<LogoutCommand>))]
    [InlineData(typeof(IValidator<ChangePasswordCommand>))]
    [InlineData(typeof(IValidator<RequestPasswordResetCommand>))]
    [InlineData(typeof(IValidator<ResetPasswordCommand>))]
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
    [InlineData(typeof(IValidator<CreateTodoListCommand>))]
    [InlineData(typeof(IValidator<RegisterCommand>))]
    [InlineData(typeof(IValidator<LoginCommand>))]
    [InlineData(typeof(IValidator<LogoutCommand>))]
    public void EachValidator_IsRegisteredExactlyOnce(Type validatorType)
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        services.Count(descriptor => descriptor.ServiceType == validatorType).ShouldBe(1);
    }

    /// <summary>
    /// Not a use case, so the marker-based discovery never reaches it: it has to be bound by hand,
    /// and this is the one place that checks the hand-written line was not forgotten.
    /// </summary>
    [Fact]
    public void TodoListAccess_IsRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        services.Single(descriptor => descriptor.ServiceType == typeof(ITodoListAccess))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// The layer has one entry point, so a host cannot wire up half an application by forgetting a
    /// vertical: the auth use cases arrive with the to-do list ones.
    /// </summary>
    [Fact]
    public void TheSingleEntryPoint_ComposesEveryVertical()
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(ICreateTodoListUseCase));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IRegisterUseCase));
    }

    [Fact]
    public void TheEntryPoint_Rejects_ANullServiceCollection() =>
        Should.Throw<ArgumentNullException>(() => ServiceRegistration.AddApplicationLayer(null!));

    [Fact]
    public void TheDiscovery_Rejects_ANullAssembly() =>
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddUseCasesFrom(null!));

    /// <summary>
    /// Nothing in this layer reads settings, so the entry point takes no <c>IConfiguration</c> —
    /// asking for configuration it does not use would invite the infrastructure knowledge the layer
    /// exists to avoid.
    /// </summary>
    [Fact]
    public void TheEntryPoint_AsksForNothingButTheServiceCollection() =>
        typeof(ServiceRegistration)
            .GetMethod(nameof(ServiceRegistration.AddApplicationLayer))!
            .GetParameters().Length.ShouldBe(1);

    /// <summary>
    /// Scope validation plus eager building means a missing dependency fails here rather than at
    /// the first request.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddApplicationLayer();

        services.AddScoped(_ => Substitute.For<ITodoListRepository>());
        services.AddScoped(_ => Substitute.For<ITodoListQueries>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddScoped(_ => Substitute.For<IDateTimeProvider>());
        services.AddScoped(_ => Substitute.For<IEmailSender>());
        services.AddScoped(_ => Substitute.For<IUserAccounts>());
        services.AddScoped(_ => Substitute.For<IUserProfiles>());
        services.AddScoped(_ => Substitute.For<IEmailConfirmationTokens>());
        services.AddScoped(_ => Substitute.For<IPasswordResetTokens>());
        services.AddScoped(_ => Substitute.For<IPasswordResetEmailComposer>());
        services.AddScoped(_ => Substitute.For<IAccessTokenIssuer>());
        services.AddScoped(_ => Substitute.For<IRefreshTokenGrants>());
        services.AddScoped(_ => Substitute.For<IConfirmationEmailComposer>());
        services.AddScoped(_ => Substitute.For<IIdempotencyStore>());
        services.AddScoped(_ => Substitute.For<IRefreshTokenMaintenance>());
        services.AddScoped(_ => Substitute.For<ISecurityEventLog>());

        // The layer's domain-event consumer takes an ILogger, which every real host supplies.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}

internal sealed class UseCaseWithNoContract : IUseCase<Guid, Result>
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

internal interface IFirstContract : IUseCase<Guid, Result>;

internal interface ISecondContract : IUseCase<Guid, Result>;

internal sealed class UseCaseWithTwoContracts : IFirstContract, ISecondContract
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
