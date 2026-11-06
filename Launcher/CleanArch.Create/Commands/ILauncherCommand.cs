using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;

namespace CleanArch.Create.Commands;

public interface ILauncherCommand
{
    string Name { get; }

    IReadOnlyList<string> Summary { get; }

    string Help(TemplateSource source);

    string? Offer(ProjectFacts project);

    void Run(Session session);
}
