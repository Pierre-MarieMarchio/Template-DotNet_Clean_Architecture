using System.Diagnostics;

namespace AppTemplate.PresentationApp.Common.Sessions;

internal sealed class SystemBrowser : ISystemBrowser
{
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
    }
}
