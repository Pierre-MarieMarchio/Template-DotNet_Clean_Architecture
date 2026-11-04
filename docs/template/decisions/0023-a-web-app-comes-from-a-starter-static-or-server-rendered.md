# 0023. A web app comes from a starter, static or server-rendered

Status: Accepted; supersedes [0008](0008-a-web-app-comes-from-a-starter.md) and [0011](0011-a-starter-may-render-on-the-server.md)

## Context

A generated project may start with a web app, and add more later with `cleanarch-webapp`. A
framework's starter, its build settings and its dependency set change with every release, so a copy
kept in the template would age between two template releases, and keeping it would be keeping a
second product: the app comes from the framework's official CLI at bootstrap
([0003](0003-framework-apps-come-from-official-clis.md) gives the reasons). What surrounds the app,
its `Dockerfile`, `nginx.conf`, `compose.yml` and `README.md`, is the same for every framework but
for the names, the default port, the build's output folder and a few framework habits, so a folder
per framework would repeat every wiring fix once per framework and cost a copy per new value.

Some generators write a server-rendered app as readily as a single-page one: the Angular CLI with
`--ssr`, React Router by default. A server-rendered app runs on Node rather than nginx and needs a
nonce per response rather than a hash computed at build, which
[ADR 0024 of the project](../../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md)
records. The template has to offer that form without doubling its values.

## Decision

- **One wiring, a catalogue of starters.** `Src/Web/apps/FrameworkSite` holds the wiring every
  framework app shares, under one set of rename tokens. Each `--web` value with a CLI is a starter,
  a folder under `.template.config/starters` named after the value, holding
  `FrameworkSite/scaffold.json` and `FrameworkSite/overlay/`. The project template and
  `cleanarch-webapp` copy the wiring under the `webFramework` symbol and the chosen starter into the
  app's folder; `web-scaffold` consumes the starter at bootstrap.
- **A starter is a pinned CLI with its arguments, its merges and its overlay**: the CLI's package and
  exact version, the arguments `pnpm dlx` passes it, the JSON merges into the files it writes, and
  the files laid over its starter where no setting reaches. The template keeps no framework code.
- **A `--web` value names its generator and never changes meaning, and a framework has one value.**
  `vite-react` is `create-vite` with `react-ts`, `vite-vue` with `vue-ts`, `vite-svelte` with
  `svelte-ts`, `react-router` is `create-react-router`, `angular` is `ng new`. A second generator for
  a framework the catalogue holds is not added.
- **The wiring differs by framework only through markers**: a difference is a marker on that
  framework's symbol in the shared file, never a second copy of the file.
- **A single-page starter keeps one output contract**, which the wiring relies on:
  - `pnpm build` writes a static site into the app's `dist/` folder, with an `index.html` at its
    root, which the shared `Dockerfile` copies into nginx; React Router's builder keeps the site in
    a `client` folder of its output, which the `Dockerfile` copies instead by a marker on its symbol;
  - an inline script in the built page runs only by its hash. The shared Content-Security-Policy
    says `script-src 'self'`; a starter whose page carries inline scripts, React Router's, gets by a
    marker on its own symbol a step of the image build that computes the SHA-256 of each inline
    script of the built page and adds those hashes to `script-src`. Never `'unsafe-inline'` for
    scripts, and never a nonce, which needs a server writing each response;
  - a starter whose build inlines styles has `'unsafe-inline'` in `style-src` by a marker on its own
    symbol, as Angular does;
  - a development server that relays `/api/` and `/health` to the API (`/api` for Angular's proxy),
    so the browser keeps one origin;
  - a package named by the app's kebab token, depending on `@app-template/api-client`, with the
    `build` and `test` scripts the workspace runs; a `lint` script is the CLI's own, and the
    workspace's `lint` skips an app without one.
- **Rendering is an option of its own, not a `--web` value.** `--web-rendering spa|ssr` on
  `cleanarch-webapi` and `--rendering spa|ssr` on `cleanarch-webapp`, `spa` by default; the
  generator is the same with either value, and the rendering names the generator's own flag or
  template.
- **A starter offers `ssr` by holding it.** Its folder holds `ssr/FrameworkSite/`, a
  `scaffold.json` and an `overlay/` of its own, beside `FrameworkSite/`. Both templates copy the
  starter's folder without `ssr/`, leave its `FrameworkSite/` out under `webServerRendering`, and
  copy its `ssr/` folder under the starter's symbol and `webServerRendering`, into the same app
  folder. `angular` holds one, `ng new --ssr` with every route rendered per request, and
  `react-router` another, its `node-custom-server` template with `ssr: true`.
- **`ssr` is refused for every starter that does not hold it**, `--web none` included, by
  `serverRenderingNeedsAServerStarter`, a hidden required parameter whose condition `refusedOptions`
  repeats ([0001](0001-options-remove-parts-and-refuse-combinations.md)). Every authentication mode
  takes `ssr`.
- **A server-rendered starter keeps its own output contract.** `pnpm build` writes into `dist/` the
  server entry the image runs with Node, `dist/server/server.mjs`, holding every dependency of the
  server, and the browser files it serves. The shared `Dockerfile` ends, under `webServerRendering`,
  on the pinned Node image running that entry as the unprivileged `node` user on port 8080, with the
  healthcheck nginx has, and `nginx.conf` is left out by a path condition. The server does what nginx
  does for the others: the relay, the page headers, the cache rules and the Content-Security-Policy,
  whose `script-src` takes a nonce per response and, for Angular, the hashes of the inline scripts
  its build writes into the page template, computed when the server starts. With accounts, the
  overlay's server keeps the session, as the project's ADR 0024 describes.
- **A .NET starter keeps the same place and the same server.** `blazor-wasm` is a starter of kind
  `dotnet` ([0009](0009-a-dotnet-project-comes-from-a-starter.md)): `web-scaffold` runs
  `dotnet new blazorwasm` for it and adds the project to the solution, and its folder holds the
  project file and `Program.cs` the project owns beside `scaffold.json`. Its output is the published
  `wwwroot`, built by the .NET SDK from the repository root: the shared `Dockerfile` and `nginx.conf`
  take it by a marker on its symbol, with the hash step for its import map, `'wasm-unsafe-eval'`,
  which WebAssembly needs, and a year of cache for `_framework/`, whose file names carry their
  fingerprints. Its `compose.yml` and `README.md` are its own, and the project template leaves the
  shared two out under the same symbol.
- `TemplatePackagingTests` holds that each folder under `.template.config/starters` is a choice of
  `--web` and of `--framework`, that each framework choice is chosen by one computed symbol under
  which both templates copy one starter, that an alias's symbol also chooses the starter it copies,
  that `webFramework` reaches that symbol, that each starter holding `ssr/FrameworkSite/scaffold.json`
  is copied by both templates under `webServerRendering`, and that the refusal names exactly the
  starters that do not.

## Alternatives considered

- **One folder per framework, wiring included.** Each new value copies four files and three tokens
  per template, and a fix to the wiring lands once per framework.
- **Inline scripts allowed by `'unsafe-inline'`, or by a nonce in a static app.** The first lets any
  injected script run; the second needs a server writing a fresh nonce into each response, which
  nginx serving a static site is not.
- **Hashes written into `nginx.conf` by hand, or substituted when the container starts.** The first
  goes stale at the first change to the page; the second puts in every start a computation the image
  build does once, from the very page the image serves.
- **The hash step in every image.** It would allow an inline script that slips into any app's page
  as silently as React Router's: a starter whose page carries none keeps refusing them.
- **Starters inside `Src/Web/apps`.** The checkout would carry a descriptor per framework where
  `web-scaffold` looks for apps to create, and each generated project would need them excluded.
- **A `--web` value per rendering, `angular-ssr` beside `angular`.** Every value that renders both
  ways doubles, the launcher shows both, and each starter to come that renders both ways doubles
  again.
- **One `scaffold.json` and one overlay per starter, with markers for the rendering.** `web-scaffold`
  parses the descriptor, and the server files exist in one rendering only, which a path condition
  says better than markers.
- **Server rendering only, as each generator's default.** Angular and React Router would lose the
  static form their projects take, and with it nginx and the hashes.
- **Prerendering at build, served by nginx with the hash step.** It renders only the pages known at
  build, with no request's data, and it is open to a project that sets its routes so.
- **nginx kept in front of the Node server.** Each app would be two services, and the nonce is still
  the Node server's to write.
- **React Router's server dependencies installed in the image** with `pnpm deploy --prod`. A second
  install in the shared `Dockerfile`, pnpm's settings for deploying a workspace package whose exports
  are TypeScript sources, which Node refuses to strip under `node_modules`, and a runtime stage
  unlike Angular's; bundling keeps one.

## Consequences

- A framework costs its starter folder, its choice in both templates with its computed symbol, a
  source in each `template.json`, its default port and its acceptance leg; a server form costs its
  `ssr/` folder, a source and a modifier in each `template.json`, its value in the refusal, and its
  app in a web leg. The wiring is untouched unless the framework differs.
- Angular builds into `dist/` through a merge of `outputPath` into the angular.json its CLI writes,
  so one `Dockerfile` serves every framework.
- A change to the wiring reaches every framework app at once, and the acceptance legs prove it on
  each; the page check fails on an inline script whose hash or nonce the served policy does not
  hold, and on two responses that share a nonce.
- A single-page React Router app's policy follows its page: each image build hashes the inline
  scripts it serves, and a change to them needs no edit to `nginx.conf`.
- The launcher needs no change for a new starter or a new server form: the manifests and the refusal
  tell it where each value and `ssr` apply.
- A server-rendered app's image runs Node, which Dependabot updates through the same `docker`
  ecosystem as nginx; the web workflow's image job checks it runs unprivileged on 8080 as it does
  nginx's.

## Revisit when

A starter cannot meet its output contract without changing the wiring for every framework, a
generator's server rendering needs a runtime other than Node, or a starter's page changes its
inline scripts when it runs, which a hash computed at build cannot allow.
