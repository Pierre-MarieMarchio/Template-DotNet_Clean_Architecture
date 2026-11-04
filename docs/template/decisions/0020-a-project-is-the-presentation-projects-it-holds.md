# 0020. A project is the presentation projects it holds

Status: Accepted

## Context

A project holds one or several hosts: the HTTP API, a Blazor portal, an MCP server, a desktop
client, a worker for the background jobs. A team may want an MCP server and a Blazor portal, or a
portal and a worker without an HTTP contract at all; an option that adds one presentation beside an
API always generated cannot say so. The `dotnet new` engine takes a choice with
`allowMultipleValues`: the CLI repeats the option (`--presentation api --presentation mcp`; `api|mcp`
and `api,mcp` are refused), and a condition reads `presentation == "mcp"` as "mcp is among the
values" and `presentation != "mcp"` as "it is not", in source modifiers, markers, generated
switches and `isRequired` alike.

## Decision

- **`--presentation` names every host the project holds, the API among them.** One multi-valued
  choice: `api`, `blazor-server`, `mcp`, `avalonia`, `wpf`, `worker`; `api` by default.
  `presentationApi`, `presentationBlazorServer`, `presentationMcp`, `presentationAvalonia`,
  `presentationWpf` and `jobsWorker` name each value.
  `EveryPresentationChoice_IsAStarterTheTemplateCopiesByIt` holds one starter per value but `api`,
  copied under that value's symbol.
- **The worker is a presentation value.** The jobs run in the worker when `worker` is chosen, in the
  API otherwise, by the sources and switch symbols
  [0024](0024-the-worker-takes-the-jobs-out-of-the-api.md) describes. `workerNeedsServerDatabase` refuses the
  worker, chosen or added, with `--database sqlite`.
- **Each host has its name.** `Api`, `Portal` for `blazor-server`, `Mcp`, `Desktop` for `avalonia`
  or `wpf`, `Worker`, with no option to name them; each starter carries a token of its own
  (`PresentationAppPortal`, `PresentationAppMcp`, `PresentationAppDesktop`, with their kebab and
  upper-case forms), which a constant replaces in the project template and `-n` in
  `cleanarch-presentation`, so two starters never collide on a path, a service or a port. Avalonia
  and WPF would both write `Desktop`: `desktopIsOneFramework` refuses them together. The Blazor
  portal answers on 8090, the MCP server on 8091.
- **What calls the API brings it.** `needsApi` is true with a web app, a desktop client, or the MCP
  server under local accounts, whose access keys the API issues; `api` is `presentationApi ||
  needsApi`, and conditions everything the API owns. The launcher shows the API as answered for
  you, naming what calls it ([0026](0026-the-launcher-and-the-project-tool-read-the-templates-manifests.md)).
- **A project without the API holds none of its parts.** Under `!api` it has no `<Project>.Api`, no
  API unit or integration tests, no end-to-end suite, no load smoke, no `.http` file, no `api`
  Compose service, no `openapi`, `run`, `api-client` or `end-to-end` task, no OpenAPI document, no
  .NET client, and none of the architecture rules or pages about the HTTP surface; the hosts read
  their shared settings from `x-host-environment` in `docker-compose.yml`, and `new-feature`,
  `new-use-case` and `remove-feature` write and remove every layer of a feature but the HTTP one:
  no controller, contract or API test, and the edits and rows they make name the hosts'
  `Program.cs`, never the API; they tell the shape from whether `Src/Presentation/<Initials>.Api`
  exists. The acceptance leg `no api, blazor server, mcp and worker, external auth, no agent files`
  generates, builds and tests that shape.
- **The worker is added for the jobs.** A project holding neither the API nor the worker would run
  no job: `needsWorker` is true for it, and `jobsWorker` is `presentation == "worker" ||
  needsWorker`, so the worker comes as the API comes for its callers. The launcher shows the worker
  as answered for you, the jobs running in it, and passes `--presentation worker`.
- **`cleanarch-presentation` is told whether the project holds the API.** `--has-api`, true by
  default, conditions the lines its README lists for `presentation-join`, and
  `presentationNeedsTheApi` refuses a desktop client, or the MCP server under local accounts, in a
  project without it.
- **Every refusal keeps its shape**, a hidden parameter with a conditional `isRequired`, mirrored in
  `refusedOptions` and in the `#error` variant, under `Src/Presentation/<Initials>.Presentation.Core`,
  which every project holds.

## Alternatives considered

- **One option per host** (`--api`, `--portal`, `--mcp`, `--worker`). Six options for one question,
  "which hosts", and an order in the launcher that a single multi-select answers at once.
- **The API always generated.** A team running a portal and a worker would delete a project, its
  tests, its Compose service and the tasks around it by hand, which [0001](0001-options-remove-parts-and-refuse-combinations.md)
  says an option does instead.
- **The jobs in the API brought by the portal or the MCP server alone.** The API would come back for
  a reason nobody chose, with an HTTP contract the project does not need; the worker holds the jobs
  and nothing else.
- **A refusal of a project holding neither the API nor the worker.** It kept the choice visible, at
  the cost of a question asked again for an answer only one host gives; adding the worker answers it
  the way the API is added for its callers, and the launcher shows it.
- **The jobs in whichever server host is chosen.** Every server host would carry the jobs' sources,
  settings and leases under a condition of its own, for a shape the worker already gives.
- **A name option per host.** Five names to ask for hosts whose project names `cleanarch-presentation`
  sets anyway when one is added later.

## Consequences

- A shape that wants the API beside another host names it: `--presentation blazor-server` alone is
  a portal without the API, which gets the worker for the jobs.
- A project without the API has no HTTP contract to test end to end and no host that migrates in
  Development: its first-run page applies the migrations with `dotnet ef database update`.
- Two server hosts share one compose environment block and each keeps its own service and port.
  The `several hosts, local auth, notes` leg starts the API, the portal, the MCP server and the
  worker together.

## Revisit when

A host needs a name of its own at generation, a new host calls the API, or the generator can write
a feature into a host other than the API.
