using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;

namespace CleanArch.Create.Commands.Join;

public sealed class PresentationCommand : JoinCommand
{
    public override string Name => "presentation";

    public override IReadOnlyList<string> Summary => ["add a presentation project to that project,", "make the edits its README lists, then create its pages"];

    protected override TemplateKind Kind => TemplateKind.Presentation;

    protected override string Subject => "presentation project";

    protected override string Purpose => "adds a presentation project to the project you're in, makes the edits its README lists, then creates its pages.";

    protected override string Item => "presentation";

    public override string? Offer(ProjectFacts project) => $"add a presentation project to {project.Name}";

    protected override void Add(ItemAdditions additions, ProjectFacts project) => additions.Presentation(project);
}
