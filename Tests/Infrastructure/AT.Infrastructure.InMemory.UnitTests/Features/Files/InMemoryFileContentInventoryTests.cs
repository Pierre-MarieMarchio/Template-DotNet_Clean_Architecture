using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

public sealed class InMemoryFileContentInventoryTests : FileContentInventoryContract, IDisposable
{
    private readonly ServiceProvider _provider = FileContentHost.Compose();

    private readonly IServiceScope _scope;

    public InMemoryFileContentInventoryTests() => _scope = _provider.CreateScope();

    protected override IFileContentStore Store => FileContentHost.StoreIn(_scope);

    protected override IFileContentInventory Inventory => FileContentHost.InventoryIn(_scope);

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
