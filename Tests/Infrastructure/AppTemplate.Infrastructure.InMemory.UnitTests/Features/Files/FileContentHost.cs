using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The module composed on its own, and the file doubles reached the way a request reaches them:
/// out of a scope, behind their ports. The doubles are internal, so this is also the only way to
/// reach them at all — which is the arrangement, not an obstacle: a test that named
/// <c>InMemoryFileContentStore</c> would be asserting on the double instead of on the port it stands
/// behind, and would not notice the day the module stopped registering it.
/// </summary>
internal static class FileContentHost
{
    internal static ServiceProvider Compose() =>
        new ServiceCollection().AddInMemoryModule().BuildServiceProvider(validateScopes: true);

    internal static IFileContentStore StoreIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFileContentStore>();

    internal static IFileContentInventory InventoryIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFileContentInventory>();

    internal static IFileDirectTransfer TransferIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFileDirectTransfer>();

    internal static IFileContentScanner ScannerIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFileContentScanner>();

    internal static StoredObjects BucketOf(IServiceProvider provider) =>
        provider.GetRequiredService<StoredObjects>();

    internal static ArrangedScans ScansOf(IServiceProvider provider) =>
        provider.GetRequiredService<ArrangedScans>();

    internal static FixedDateTimeProvider ClockOf(IServiceProvider provider) =>
        provider.GetRequiredService<FixedDateTimeProvider>();
}
