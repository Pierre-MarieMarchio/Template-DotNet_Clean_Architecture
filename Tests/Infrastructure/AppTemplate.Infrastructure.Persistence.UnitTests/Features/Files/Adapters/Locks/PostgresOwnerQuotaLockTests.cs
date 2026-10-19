using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Adapters.Locks;

/// <summary>
/// What can be said about the PostgreSQL quota lock without a server: its key and its registration.
/// The lock itself is exercised against PostgreSQL in the integration tests.
/// </summary>
public sealed class PostgresOwnerQuotaLockTests
{
    private static readonly UserId _owner = UserId.Create(Guid.Parse("01890a5d-ac96-774b-bcce-b302099a8057"));
    private static readonly FileCategory _document = FileCategory.Create("document");

    /// <summary>
    /// Every host must compute the same key. The expected value is the first eight bytes, big-endian,
    /// of SHA-256 over <c>files.quota:{owner}:{category}</c>, computed outside .NET.
    /// </summary>
    [Fact]
    public void TheKey_IsTheNamespacedDigestOfOwnerAndCategory() =>
        PostgresOwnerQuotaLock.KeyOf(_owner, _document).ShouldBe(5650554713523010619L);

    [Fact]
    public void AnotherCategoryOrOwner_HasAnotherKey()
    {
        long key = PostgresOwnerQuotaLock.KeyOf(_owner, _document);

        PostgresOwnerQuotaLock.KeyOf(_owner, FileCategory.Create("image")).ShouldNotBe(key);
        PostgresOwnerQuotaLock.KeyOf(UserId.Create(Guid.CreateVersion7()), _document).ShouldNotBe(key);
    }

    [Fact]
    public void ThePersistenceModule_RegistersOneLockForTheWholeProcess()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceModule(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IOwnerQuotaLock>()
            .ShouldBeOfType<PostgresOwnerQuotaLock>()
            .ShouldBeSameAs(provider.GetRequiredService<IOwnerQuotaLock>());
    }
}
