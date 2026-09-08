using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests;

/// <summary>
/// The registration mechanisms themselves: what a use case has to look like for the container to
/// bind it, and what the entry points refuse.
/// </summary>
public sealed class ApplicationCoreModuleTests
{
    private sealed class UseCaseWithNoContract : IUseCase<Guid, Result>
    {
        public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private interface IFirstContract : IUseCase<Guid, Result>;

    private interface ISecondContract : IUseCase<Guid, Result>;

    private sealed class UseCaseWithTwoContracts : IFirstContract, ISecondContract
    {
        public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
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

    [Fact]
    public void TheDiscovery_Rejects_ANullAssembly() =>
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddUseCasesFrom(null!));
}
