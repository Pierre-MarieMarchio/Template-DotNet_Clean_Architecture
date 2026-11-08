using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

internal static class ResourceReaperSwitch
{
    [ModuleInitializer]
    internal static void TurnOff() => TestcontainersSettings.ResourceReaperEnabled = false;
}
