namespace CleanArch.Cli;

internal static class ExternalCommand
{
    internal static Func<ExternalCall, TextWriter, TextWriter, int>? Handler { get; set; }

    public static IProcessLauncher Launcher { get; } = new HandledOrSystem();

    public static string LineOf(ExternalCall call) => call.Line;

    public static int RunAnnounced(ExternalCall call, TextWriter output, TextWriter error) => Launcher.Launch(call, output, error);

    private sealed class HandledOrSystem : IProcessLauncher
    {
        private readonly SystemProcessLauncher _system = new();

        public int Launch(ExternalCall call, TextWriter output, TextWriter error) =>
            Handler is { } handler ? handler(call, output, error) : _system.Launch(call, output, error);
    }
}
