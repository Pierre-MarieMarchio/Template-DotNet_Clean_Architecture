using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

internal static class ResourceReaperSwitch
{
    [ModuleInitializer]
    internal static void TurnOff() => TestcontainersSettings.ResourceReaperEnabled = false;
}
