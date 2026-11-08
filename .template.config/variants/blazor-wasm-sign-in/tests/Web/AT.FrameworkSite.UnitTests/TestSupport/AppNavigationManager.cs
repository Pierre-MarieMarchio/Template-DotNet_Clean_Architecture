using Microsoft.AspNetCore.Components;

namespace AppTemplate.FrameworkSite.UnitTests.TestSupport;

internal sealed class AppNavigationManager : NavigationManager
{
    public const string BaseAddress = "https://app.test/";

    public AppNavigationManager() => Initialize(BaseAddress, BaseAddress);
}
