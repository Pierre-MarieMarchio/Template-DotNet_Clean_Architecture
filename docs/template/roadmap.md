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
| `--web` | `Src/Web/apps/<Name>`, served by nginx | the API, through the generated client and the `/api` relay |
| `--presentation` | `Src/Presentation/<Name>` | the application layer in process for a server host, the API for a client |

A value names its generator and never changes meaning: `vite-react` is `create-vite` with its React
template, and `blazor-server` is `dotnet new blazor` with server interactivity.

## Coming, in order

1. **React from React Router**
   ([#115](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/115)).
   `--web react-router`, a single-page app from `create-react-router`. Its page carries inline
   scripts, which the content security policy will allow by their hash, computed when the image is
   built.
2. **A presentation project on the application layer**
   ([#116](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/116)).
   `--presentation blazor-server`, a Blazor Web App that calls the use cases in process, with the
   host composition it shares with the API. A project adds one later with a `cleanarch-presentation`
   item template and a `presentation` command of the launcher.
3. **.NET clients of the API**
   ([#117](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/117)). A
   .NET client generated from the API's OpenAPI document, then `--web blazor-wasm`, a Blazor
   WebAssembly app served beside the other web apps, and `--presentation avalonia`, a desktop app
   from Avalonia's templates.

## Later

Each group comes once the three items above have shipped, in no fixed order:

- **The other templates of `create-vite`**
  ([#118](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/118)):
  Solid, Preact, Lit, Qwik and plain TypeScript.
- **Web frameworks with their own generator**
  ([#119](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/119)):
  Ember; then SvelteKit, Nuxt, Analog and Astro in their static form, under the same hashed-script
  policy as React Router; Next last, whose static export goes against the framework's grain.
- **Desktop and mobile clients of the API**
  ([#120](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/120)):
  WPF and Windows Forms, built on a Windows runner; .NET MAUI; Uno Platform; WinUI, which has no
  `dotnet new` template and so is written by the template.
- **Server hosts on the application layer**
  ([#121](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/121)):
  MVC, Razor Pages, gRPC, an MCP server, a worker and a console.
- **Around the apps**
  ([#122](https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122)): an
  Aspire app host beside Docker Compose, Playwright end-to-end tests for the web apps, Storybook,
  and mobile apps from Expo or Capacitor on the same API client.

## Not planned

- **Server-side rendering of the web apps.** It replaces nginx with a Node server, changes the relay
  to the API and needs a nonce-based content security policy: a decision of its own, not an item of
  this list.
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
