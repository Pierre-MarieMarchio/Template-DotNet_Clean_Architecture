# 0008. A web app comes from a starter

Status: Accepted

## Context

[0003](0003-framework-apps-come-from-official-clis.md) made each framework app come from its
official CLI, and kept a whole folder per framework: a `Dockerfile`, an `nginx.conf`, a
`compose.yml` and a `README.md` beside the CLI's descriptor and overlay. Those four files were the
same for React and Angular but for the names, the default port, the build's output folder and
Angular's inline styles, and each folder was three rename tokens and an exclusion in both
`template.json` files. The roadmap adds Vue and three more `create-vite` templates, then other
generators: at one folder per framework, every wiring fix would be made once per framework, and
every new value would cost a copy of the wiring.

## Decision

- **One wiring, a catalogue of starters.** `Src/Web/apps/FrameworkSite` holds the wiring every
  framework app shares, under one set of rename tokens. Each `--web` value with a CLI is a starter,
  a folder under `.template.config/starters` named after the value, holding only
  `FrameworkSite/scaffold.json` and `FrameworkSite/overlay/`. The project template and
  `cleanarch-webapp` copy the wiring under the `webFramework` symbol and the chosen starter into the
  app's folder; `web-scaffold` consumes the starter at bootstrap, as 0003 describes.
- **A starter is a pinned CLI with its arguments, its merges and its overlay**: the CLI's package and
  exact version, the arguments `pnpm dlx` passes it, the JSON merges into the files it writes, and
  the files laid over its starter where no setting reaches. The template keeps no framework code.
- **A starter keeps one output contract**, which the wiring relies on:
  - `pnpm build` writes a static site into the app's `dist/` folder, with an `index.html` at its
    root, which the shared `Dockerfile` copies into nginx; React Router's builder keeps the site in
    a `client` folder of its output, which the `Dockerfile` copies instead by a marker on its symbol;
  - an inline script in the built page runs only by its hash. The shared Content-Security-Policy
    says `script-src 'self'`; a starter whose page carries inline scripts, React Router's, gets by
    a marker on its own symbol a step of the image build that computes the SHA-256 of each inline
    script of the built page and writes the server's configuration with those hashes added to
    `script-src`, which the image then serves. A starter without one keeps the policy as it is.
    Never `'unsafe-inline'` for scripts, never a nonce, which needs a server writing each response;
  - a starter whose build inlines styles has `'unsafe-inline'` in `style-src` by a marker on its own
    symbol, as Angular does;
  - a development server that relays `/api/` and `/health` to the API (`/api` for Angular's
    proxy), so the browser keeps one origin;
  - a package named by the app's kebab token, depending on `@app-template/api-client`, with the
    `build` and `test` scripts the workspace runs. A `lint` script is the CLI's own: `create-vite`
    writes one, the Angular CLI does not, and the workspace's `lint` skips an app without it.
- **A `--web` value names its generator and never changes meaning.** `vite-react` is `create-vite`
  with `react-ts`, `vue` is `create-vue`, `angular` is `ng new`; a different generator for the same
  framework is a new value. `react`, the name `vite-react` had first, stays as its alias until the
  next major version: one computed symbol chooses both, so both copy the same starter.
- **Server-side rendering is out of scope.** A starter builds a static site served by nginx; one
  that needs a Node server is not a starter of this catalogue.
- **The wiring differs by framework only through markers.** A difference is a marker on that
  framework's symbol in the shared file, never a second copy of the file.
- **A .NET starter keeps the same place and the same server.** `blazor-wasm` is a starter of kind
  `dotnet`, as [0009](0009-a-dotnet-project-comes-from-a-starter.md) describes: `web-scaffold` runs
  `dotnet new blazorwasm` for it and adds the project to the solution, and its folder holds the
  project file and `Program.cs` the project owns beside `scaffold.json`. Its output contract is the
  published `wwwroot`, built by the .NET SDK from the repository root, which holds the solution's
  props and the .NET client: the shared `Dockerfile` and `nginx.conf` take it by a marker on its
  symbol, with the hash step of the inline scripts for its import map, `'wasm-unsafe-eval'`, which
  WebAssembly needs, and a year of cache for `_framework/`, whose file names carry their
  fingerprints. Its `compose.yml` and `README.md` are its own, since its build context is the
  repository root and the shared ones describe a pnpm app, and the project template leaves the
  shared two out under the same symbol.
- `TemplatePackagingTests` holds that each folder under `.template.config/starters` is a choice of
  `--web` and of `--framework`, that each framework choice is chosen by one computed symbol under
  which both templates copy one starter, that an alias's symbol also chooses the starter it copies,
  and that `webFramework` reaches that symbol.

## Alternatives considered

- **One folder per framework, as 0003 had it.** Each new value copies four files and three tokens
  per template, and a fix to the wiring lands once per framework.
- **Inline scripts allowed by `'unsafe-inline'`, or by a nonce.** The first lets any injected script
  run; the second needs a server writing a fresh nonce into each response, which a static site
  served by nginx is not.
- **Hashes written into `nginx.conf` by hand, or substituted when the container starts.** The first
  goes stale at the first change to the page; the second puts a computation in every start that
  the image build does once, from the very page the image serves.
- **The hash step in every image.** It would allow an inline script that slips into any app's page
  as silently as React Router's: a starter whose page carries none keeps refusing them.
- **A `kind` field in each `scaffold.json`, ready for starters that are not pnpm apps.** No second
  kind exists yet; the field's shape waits for the first .NET starter.
- **Starters inside `Src/Web/apps`.** The checkout would carry a descriptor per framework where
  `web-scaffold` looks for apps to create, and each generated project would need them excluded.

## Consequences

- A framework costs its starter folder, its choice in both templates with its computed symbol, a
  source in each `template.json`, its default port, and its acceptance leg; the wiring is untouched
  unless the framework differs.
- Angular builds into `dist/` through a merge of `outputPath` into the angular.json its CLI writes, so one
  `Dockerfile` serves every framework.
- A change to the wiring reaches every framework app at once, and its acceptance legs prove it on
  each.
- A generated app folder holds the same files as under 0003, renamed from the same `--web-name`;
  only an Angular app's build output moves, to `dist/`.
- A React Router app's policy follows its page: each image build hashes the inline scripts it
  serves, and a change to them needs no edit to `nginx.conf`. SvelteKit, Nuxt, Analog and Astro in
  their static form, whose pages carry inline scripts too, need only their starter and that step.
- The acceptance check reads the policy each app serves and fails on an inline script whose hash
  it does not hold, so every other app still fails on any inline script.

## Revisit when

A starter cannot meet the output contract without changing the wiring for every framework, a
generator that is not a pnpm app joins the catalogue, server-side rendering becomes a choice of its
own, or a starter's page changes its inline scripts when it runs, which a hash computed at build
cannot allow.
