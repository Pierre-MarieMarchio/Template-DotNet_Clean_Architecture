namespace CleanArch.Cli.Commands.Kernel;

public sealed record ProjectFile(
    TemplateReference Template,
    string Name,
    ProjectOptions Options,
    bool Client = false,
    WebWorkspace? Web = null,
    IReadOnlyList<PresentationProject>? Presentation = null);

public sealed record TemplateReference(string Package, string Version);

public sealed record ProjectOptions(
    string? Auth = null,
    string? Database = null,
    string? Examples = null,
    bool? Files = null,
    bool? Ci = null,
    bool? Sonar = null,
    bool? AgentFiles = null,
    bool? Aspire = null);

public sealed record WebWorkspace(string Scope, IReadOnlyList<WebApp> Apps, IReadOnlyList<string> Packages);

public sealed record WebApp(string Name, string Framework, string Audience, string Rendering, int? Port = null);

public sealed record PresentationProject(string Name, string Framework);
