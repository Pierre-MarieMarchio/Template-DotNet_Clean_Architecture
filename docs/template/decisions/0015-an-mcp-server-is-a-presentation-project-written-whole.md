# 0015. An MCP server is a presentation project, written whole

Status: Accepted; amended by [0020](0020-a-project-is-the-presentation-projects-it-holds.md),
which lets it stand beside other presentation projects under the fixed name `Mcp`

## Context

An MCP server is a second host on the application layer: its tools call the use cases in process,
as a Blazor Server project's pages do, and it needs a name, a port, a Compose service, an item
template for a project generated without it, and the lines a join task makes. `--jobs worker` shows
the other way to add a host, an option of its own beside `--presentation`. Microsoft's
`dotnet new mcpserver`, from `Microsoft.McpServer.ProjectTemplates.10.0` 10.0.14, writes a server:

| Host | Generator, as checked | What it writes | What the project would keep |
|---|---|---|---|
| `--presentation mcp` | `dotnet new mcpserver --transport remote` | a `Microsoft.NET.Sdk.Web` project on `ModelContextProtocol.AspNetCore` with its own target framework, `RollForward` and `UserSecretsId`, `Program.cs` mapping a stateless Streamable HTTP endpoint at `/`, a sample random-number tool, a `.http` file, a README and the launch settings | nothing: the starter owns the project, `Program.cs`, the settings, the launch settings and the tools, and the sample goes |

## Decision

- **The MCP server is a value of `--presentation`.** `--presentation mcp` and
  `cleanarch-presentation --framework mcp` write it under the same symbols, name, port, service and
  join task as every presentation project, from `.template.config/starters/mcp`; it composes the
  use cases in process, as `blazor-server` does.
- **Its starter is written whole**, as [0013](0013-a-starter-the-generator-cannot-add-to-is-written-whole.md)
  has it, in the shape `dotnet new mcpserver --transport remote` gives at 10.0.14, its package
  version in `Directory.Packages.props`.
- **It needs an authenticated caller.** `--auth none` is refused by `mcpNeedsAccounts`, in both
  templates: every tool acts for the caller its credential names, and `--auth none` names nobody.
- **Access keys come with it.** Under `--auth local` the server's callers present access keys, and
  `accessKeys` (`localAuth && presentationMcp`) brings their slice of the Auth context and the API;
  a project without the server carries none. `cleanarch-presentation` writes the slice's own files
  and lists the lines it adds elsewhere for `presentation-join`.
- **Its name is `Mcp`.** The starter carries its own token, `PresentationAppMcp`, which the project
  template replaces with `Mcp` ([0020](0020-a-project-is-the-presentation-projects-it-holds.md)).
  `cleanarch-presentation` keeps `Portal` as `-n`'s default, which the engine fixes before it reads
  `--framework`, and its README passes `-n Mcp`.

## Alternatives considered

- **An option of its own, `--mcp`, beside any `--presentation`.** Two server hosts would share
  nothing a presentation already has: a second name, port, service, item template and join list,
  for a combination nobody has asked for yet.
- **A `dotnet` starter running `dotnet new mcpserver`.** The network and a pinned package, to keep
  nothing of what it writes.
- **`--auth none` with a server that acts for nobody.** Every owner-scoped use case would refuse it,
  and one that is not owner-scoped would answer anyone who finds the address.

## Consequences

- An MCP server and a Blazor Web App are generated together since
  [0020](0020-a-project-is-the-presentation-projects-it-holds.md), each with its own service and
  port.
- `TemplatePackagingTests` holds a presentation starter to the kind `dotnet` or `written`, and the
  two templates to the same modifiers for it; `TheStarterReadme_ListsEveryLineItsStarterWritesOutsideItsFolders`
  holds its README's join list. The legs `mcp, external auth, all examples` and
  `mcp, local auth, notes` start the stack and call it; `sqlite, external auth, all examples` adds it
  with `cleanarch-presentation`.

## Revisit when

The generator comes to write something the project would keep.
