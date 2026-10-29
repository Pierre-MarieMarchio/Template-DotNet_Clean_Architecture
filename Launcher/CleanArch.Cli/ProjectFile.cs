namespace CleanArch.Cli;

public sealed record ProjectFile(
    TemplateReference Template,
    string Name,
    ProjectOptions Options,
    bool Client = false,
    WebWorkspace? Web = null,
    PresentationProject? Presentation = null);

public sealed record TemplateReference(string Package, string Version);

public sealed record ProjectOptions(
    string? Auth = null,
    string? Database = null,
    string? Examples = null,
    bool? Files = null,
    string? Jobs = null,
    bool? Ci = null,
    bool? Sonar = null,
    bool? AgentFiles = null);

public sealed record WebWorkspace(string Scope, IReadOnlyList<WebApp> Apps, IReadOnlyList<WebPackage> Packages);

public sealed record WebApp(string Name, string Framework, string Audience, string Rendering);

public sealed record WebPackage(string Name);

public sealed record PresentationProject(string Name, string Framework);
