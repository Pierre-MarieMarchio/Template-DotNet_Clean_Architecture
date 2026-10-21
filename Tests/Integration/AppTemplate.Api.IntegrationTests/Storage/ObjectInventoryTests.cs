using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class ObjectInventoryTests(ObjectStoreFixture fixture) : FileContentInventoryContract
{
    protected override IFileContentStore Store => fixture.Content;

    protected override IFileContentInventory Inventory => fixture.Inventory;

    protected override string NewPrefix() => ObjectStoreFixture.UniquePrefix("inventory");
}
