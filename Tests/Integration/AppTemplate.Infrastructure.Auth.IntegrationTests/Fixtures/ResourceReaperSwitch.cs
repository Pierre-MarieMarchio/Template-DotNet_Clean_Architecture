using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

/// <summary>
/// Turns Testcontainers' resource reaper (Ryuk) off before any fixture starts a container. Every
/// fixture removes its own containers in <c>DisposeAsync</c>; what goes is only the clean-up of a
/// run killed outright, which TESTING.md says how to do by hand.
/// </summary>
/// <remarks>
/// Ryuk is declared ready once its "Started" line is read from the container logs, which are
/// queried from the container's creation time on. A wall clock that steps backwards between the
/// two — as a WSL 2 machine's does every few seconds — stamps that line before the creation, so it
/// is never read: the start waits sixty seconds and fails the whole fixture that triggered it.
/// </remarks>
internal static class ResourceReaperSwitch
{
    [ModuleInitializer]
    internal static void TurnOff() => TestcontainersSettings.ResourceReaperEnabled = false;
}
