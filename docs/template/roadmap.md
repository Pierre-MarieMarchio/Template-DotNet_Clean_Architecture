# Roadmap

What is coming to the template, in the order it is built, and what it leaves out on purpose. Each
item ships on its own; a shipped item leaves this page for [the changelog](../../CHANGELOG.md).

## The rule every item follows

A web app or a presentation project comes from its framework's official generator when there is
one: `bootstrap` runs it at a pinned version, then the template wires its output in and leaves the
rest to the project, as [ADR 0008](decisions/0008-a-web-app-comes-from-a-starter.md) does
for the web apps today. A framework without a generator gets its project written by the template,
as the placeholder app is.

Once generated, a project is wired either to the HTTP API or, when it runs on the server, straight
to the application layer:

| Option | Where it lives | Wired to |
|---|---|---|
| `--web` | `Src/Web/apps/<Name>`, served by nginx or, rendered on the server, by its own Node server | the API, through the generated client and the `/api` relay |
| `--presentation` | `Src/Presentation/<Project>.<Name>` | the application layer in process for a server host, the API for a client |

A value names its generator and never changes meaning: `vite-react` is `create-vite` with its React
template, and `blazor-server` is `dotnet new blazor` with server interactivity.

## Coming

Each group comes in no fixed order:

- **The other templates of `create-vite`**
  ([#118](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/118)):
  Solid, Preact, Lit, Qwik and plain TypeScript.
- **Web frameworks with their own generator**
  ([#119](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/119)):
  Ember; then SvelteKit, Nuxt, Analog and Astro in their static form, each needing only its
  starter and a marker giving it the hash step React Router's image build already has, their server
  form then an `ssr/` folder of that starter, as Angular's and React Router's are; Next last, whose static export goes against the
  framework's grain.
- **Desktop and mobile clients of the API**
  ([#120](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/120)):
  WPF and Windows Forms, built on a Windows runner; .NET MAUI; Uno Platform; WinUI, which has no
  `dotnet new` template and so is written by the template.
- **Server hosts on the application layer**
  ([#121](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/121)):
  MVC, Razor Pages, gRPC and a console.
- **Around the apps**
  ([#122](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122)): an
  Aspire app host beside Docker Compose, Playwright end-to-end tests for the web apps, Storybook,
  and mobile apps from Expo or Capacitor on the same API client.
- **A project's tasks from a pinned .NET tool**
  ([#163](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/163)):
  `Tools/Tasks.cs` and its generators leave the generated tree for `dotnet cleanarch`, pinned
  beside `dotnet-ef`, which a team changes through its configuration, its code templates, its
  local commands or the ejection of one command, as
  [ADR 0017](decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md) proposes.

## Not planned

- **A generator command of the user's choosing.** Its output could be neither tested nor held to the
  content security policy.
- **A desktop app wired straight to the application layer.** It would carry the database's
  credentials onto every user's machine.
- **MySQL**, until Entity Framework Core 10 has an official MySQL provider.

## Proposing an item

Ask for a framework, a host or a change of order through
[the repository's issue forms](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/new/choose);
each item above has its issue, labelled `roadmap`, where its discussion happens.
[Contributing](../../CONTRIBUTING.md#working-on-the-template) says how a change reaches the template.
