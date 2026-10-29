using AppTemplate.PresentationApp.Common.Hosting;
using Avalonia;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.PresentationApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var host = AppHost.Build(args);
        host.Start();

        return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(services: null);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
