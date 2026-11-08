using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

internal sealed record SetupContext(IProcessRunner Processes, string Root, string Version, Action<string>? Echo, Git Git)
{
    public string GeneratedMessage => $"chore: generate from cleanarch-webapi {Version}";
}
