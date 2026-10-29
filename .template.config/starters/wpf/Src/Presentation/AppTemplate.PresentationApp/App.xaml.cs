using System.Windows;
using AppTemplate.PresentationApp.Core.Common.Hosting;
using AppTemplate.PresentationApp.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.PresentationApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var host = AppHost.Build(e.Args);
        host.Start();
        Exit += (_, _) => host.Dispose();

        MainWindow = new MainWindow(host.Services.GetRequiredService<MainViewModel>());
        MainWindow.Show();
    }
}
