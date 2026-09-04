using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The module composed on its own, and the two file doubles reached the way a request reaches them:
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

    internal static IFileContentInspector InspectorIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFileContentInspector>();

    internal static StoredObjects BucketOf(IServiceProvider provider) =>
        provider.GetRequiredService<StoredObjects>();

    internal static ArrangedInspections InspectionsOf(IServiceProvider provider) =>
        provider.GetRequiredService<ArrangedInspections>();

    internal static FixedDateTimeProvider ClockOf(IServiceProvider provider) =>
        provider.GetRequiredService<FixedDateTimeProvider>();
}
