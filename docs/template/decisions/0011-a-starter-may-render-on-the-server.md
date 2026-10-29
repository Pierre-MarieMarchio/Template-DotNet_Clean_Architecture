# 0011. A starter may render on the server

Status: Accepted

## Context

[0008](0008-a-web-app-comes-from-a-starter.md) left server-side rendering out of the catalogue: a
starter builds a static site that nginx serves, and its inline scripts run by their hash. Some of the
generators the catalogue pins write a server-rendered app as readily as a single-page one, and
letting their own choices through is why the apps come from them. At the pinned versions:

| Starter | Generator | Server rendering |
|---|---|---|
| `angular` | `@angular/cli` 22.2.3, `ng new` | `--ssr`: an Express `src/server.ts` on `AngularNodeAppEngine`, `outputMode: "server"`, every route prerendered by default |
| `react-router` | `create-react-router` 8.5.0, React Router's templates at the pinned commit | `default` renders on the server unless `ssr: false`, served by `react-router-serve`; `node-custom-server` is the same app behind an Express server of its own |
| `vue` | `create-vue` 3.24.0 | none: no flag renders on the server |
| `vite-react`, `vite-vue`, `vite-svelte` | `create-vite` 9.3.1 | none: every template is client-only |
| `blazor-wasm` | `dotnet new blazorwasm` | none: the server-rendered Blazor app is `dotnet new blazor`, the presentation project's |
| `placeholder` | none | none: a static page |

A server-rendered app runs on Node and needs a per-request nonce rather than a build-time hash;
[ADR 0024 of the project](../../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md)
records those choices. This one records how the template offers it.

## Decision

- **Rendering is an option of its own, not a `--web` value.** `--web-rendering spa|ssr` on
  `cleanarch-webapi` and `--rendering spa|ssr` on `cleanarch-webapp`, `spa` by default. The
  generator is the same with either value, so a `--web` value still names one generator; the
  rendering names the generator's own flag or template.
- **A starter offers `ssr` by holding it.** Its folder holds `ssr/FrameworkSite/`, a
  `scaffold.json` and an `overlay/` of its own, beside `FrameworkSite/`. Both templates copy the
  starter's folder without `ssr/`, leave its `FrameworkSite/` out under `webServerRendering`, and
  copy its `ssr/` folder under the starter's symbol and `webServerRendering`, into the same app
  folder. `angular` holds one: `ng new --ssr`, with every route rendered per request. `react-router`
  holds another: the `node-custom-server` template at the commit its single-page form takes, with
  `ssr: true`, its Express app replaced by the server below and its `server.js` by a development
  server alone.
- **`ssr` is refused for every starter that does not hold it**, `--web none` included, by
  `serverRenderingNeedsAServerStarter`: a hidden required parameter whose condition
  `refusedOptions` repeats, as [0001](0001-options-remove-parts-and-refuse-combinations.md) has it.
  The launcher reads it ([0004](0004-the-launcher-reads-the-templates-manifests.md)) and asks the
  rendering only with a web app, so after a single-page-only starter the question has one choice
  left and is answered with it unasked. Every authentication mode takes `ssr`.
- **A server-rendered starter keeps its own output contract.** `pnpm build` writes into the app's
  `dist/` folder the server entry the image runs with Node, `dist/server/server.mjs`, holding every
  dependency of the server, and the browser files it serves. Angular's builder bundles them;
  React Router's overlay names the entry `server.mjs` and bundles every dependency into the server
  build, from `server/app.ts`, which listens when Node runs it. The shared `Dockerfile` ends, under
  `webServerRendering`, on the pinned Node image running that entry as the unprivileged `node` user
  on port 8080, with the healthcheck nginx has; `nginx.conf` is left out by a path condition. The
  server does what nginx did: the `/api/` and `/health` relay, the page headers, the cache rules,
  and the Content-Security-Policy, whose `script-src` takes a nonce per response and, for Angular,
  the hashes of the inline scripts the build writes into the page template, computed when the
  server starts; React Router's build writes no page template, and its single-page form alone keeps
  the hash step of the image build. With accounts, the overlay's server keeps the
  session, through `serverSessions` of `@app-template/api-client/server` for local accounts and
  `providerSessions` for an external provider, as the project's ADR 0024 describes; with an external
  provider the app's `compose.yml` passes it the provider and the client, and `.env.example` gains
  the app's public origin. 0008's "never a nonce" stays true of the static apps, which have no server to
  write one.
- **No new acceptance leg.** The smallest web leg adds its second app as a server-rendered Angular
  app without accounts, the back-office leg its Angular back office with local accounts, and the
  Angular leg with an external provider generates its first app rendered on the server; the
  every-starter leg adds a React Router app rendered on the server, with local accounts, beside its
  single-page one, each entry of its `more_apps` taking `:ssr` after its framework. The
  page check learns the nonce: an inline script runs by its hash or by the nonce its response's
  policy names, two responses never share one, and a data block such as Angular's transferred state
  is no script; a server-rendered app with accounts answers its own `403` to a `POST` without
  `X-CSRF`, and relays one that carries it; with an external provider it answers its own `400` to a
  sign-in callback it did not start, and sends `/session/sign-in` to the provider, or answers `502`
  while the provider cannot be reached, as in that leg's stack. The refusal job asserts `ssr`
  refused for a single-page-only starter.
- `TemplatePackagingTests` holds that each starter holding `ssr/FrameworkSite/scaffold.json` is
  copied by both templates under `webServerRendering`, and that the refusal names exactly the
  starters that do not.

## Alternatives considered

- **A `--web` value per rendering, `angular-ssr` beside `angular`.** Every value that renders both
  ways doubles, the launcher shows both, and the starters to come that render both ways, SvelteKit,
  Nuxt and Analog, would double again.
- **One `scaffold.json` and one overlay, with markers.** `web-scaffold` parses the descriptor, and
  the server files exist in one rendering only, which a path condition says better than markers.
- **Server rendering only, as each generator's default.** Angular and React Router would lose the
  static form their projects already take, and with it nginx and the hashes.
- **Prerendering at build, served by nginx with the hash step.** It renders only pages known at
  build, with no request's data, and it is already open to a project that sets its routes so.
- **nginx kept in front of the Node server.** Each app would be two services, and the nonce is still
  the Node server's to write.
- **React Router's server dependencies installed in the image**, as the CLI's own Dockerfile does,
  with `pnpm deploy --prod`. A second install in the shared `Dockerfile`, pnpm's settings for
  deploying a workspace package whose exports are TypeScript sources, which Node refuses to strip
  under `node_modules`, and a runtime stage unlike Angular's; bundling keeps one.

## Consequences

- A starter that renders on the server costs its `ssr/` folder, a source and a modifier in each
  `template.json`, its value in the refusal, and its app in a web leg; the shared wiring changes only
  under `webServerRendering`.
- The launcher needs no change for a new server-rendered starter: the refusal tells it where `ssr`
  applies.
- A server-rendered app's image runs Node, which Dependabot updates through the same `docker`
  ecosystem as nginx; the web workflow's image job checks it runs unprivileged on 8080 as it does
  nginx's.
- 0008's statement that server-side rendering is out of scope, and its first output contract, hold
  for the single-page form only.

## Revisit when

A starter's server form cannot run behind the shared `Dockerfile` without changing it for every
starter, or a generator's server rendering needs a runtime other than Node.
