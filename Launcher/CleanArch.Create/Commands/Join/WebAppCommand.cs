using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;

namespace CleanArch.Create.Commands.Join;

public sealed class WebAppCommand : JoinCommand
{
    public override string Name => "web-app";

    public override IReadOnlyList<string> Summary => ["add a web app to the project you're in;", "a Blazor WebAssembly app also makes the edits its README lists"];

    protected override TemplateKind Kind => TemplateKind.WebApp;

    protected override string Subject => "web app";

    protected override string Purpose => "adds a web app to the project you're in; a Blazor WebAssembly app also makes the edits its README lists.";

    protected override string Item => "app";

    public override string? Offer(ProjectFacts project) => $"add a web app to {project.Name}";

    protected override void Add(ItemAdditions additions, ProjectFacts project) => additions.WebApp(project);
}
