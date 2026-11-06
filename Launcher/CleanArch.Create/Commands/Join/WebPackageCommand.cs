using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;

namespace CleanArch.Create.Commands.Join;

public sealed class WebPackageCommand : JoinCommand
{
    public override string Name => "web-package";

    public override IReadOnlyList<string> Summary => ["add a shared package to that project's web workspace"];

    protected override TemplateKind Kind => TemplateKind.WebPackage;

    protected override string Subject => "web package";

    protected override string Purpose => "adds a shared package to the web workspace of the project you're in.";

    protected override string Item => "package";

    public override string? Offer(ProjectFacts project) => project.HasWorkspace ? $"add a package to {project.Name}'s web workspace" : null;

    protected override void Add(ItemAdditions additions, ProjectFacts project) => additions.WebPackage(project);
}
