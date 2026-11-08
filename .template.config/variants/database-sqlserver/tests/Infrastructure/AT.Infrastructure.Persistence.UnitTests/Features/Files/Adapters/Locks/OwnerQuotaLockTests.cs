using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Adapters.Locks;

public sealed class OwnerQuotaLockTests
{
    private static readonly UserId _owner = UserId.Create(Guid.Parse("01890a5d-ac96-774b-bcce-b302099a8057"));
    private static readonly FileCategory _document = FileCategory.Create("document");

    [Fact]
    public void TheKey_IsTheNamespacedNameOfOwnerAndCategory() =>
        OwnerQuotaLock.KeyOf(_owner, _document).ShouldBe("files.quota:01890a5d-ac96-774b-bcce-b302099a8057:document");

    [Fact]
    public void AnotherCategoryOrOwner_HasAnotherKey()
    {
        string key = OwnerQuotaLock.KeyOf(_owner, _document);

        OwnerQuotaLock.KeyOf(_owner, FileCategory.Create("image")).ShouldNotBe(key);
        OwnerQuotaLock.KeyOf(UserId.Create(Guid.CreateVersion7()), _document).ShouldNotBe(key);
    }

    [Fact]
    public void ThePersistenceModule_RegistersOneLockForTheWholeProcess()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=unused;User Id=unused;Password=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceModule(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IOwnerQuotaLock>()
            .ShouldBeOfType<OwnerQuotaLock>()
            .ShouldBeSameAs(provider.GetRequiredService<IOwnerQuotaLock>());
    }
}
