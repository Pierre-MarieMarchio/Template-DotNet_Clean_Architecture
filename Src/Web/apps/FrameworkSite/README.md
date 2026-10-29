# FrameworkSite

<!--#if ((webViteReact || webReactRouter) && adminAudience)-->
A React back office over the API, for administrators.
<!--#endif-->
<!--#if ((webViteReact || webReactRouter) && !adminAudience)-->
A React front end over the API.
<!--#endif-->
<!--#if ((webVue || webViteVue) && adminAudience)-->
A Vue back office over the API, for administrators.
<!--#endif-->
<!--#if ((webVue || webViteVue) && !adminAudience)-->
A Vue front end over the API.
<!--#endif-->
<!--#if (webViteSvelte && adminAudience)-->
A Svelte back office over the API, for administrators.
<!--#endif-->
<!--#if (webViteSvelte && !adminAudience)-->
A Svelte front end over the API.
<!--#endif-->
<!--#if (webAngular && adminAudience)-->
An Angular back office over the API, for administrators.
<!--#endif-->
<!--#if (webAngular && !adminAudience)-->
An Angular front end over the API.
<!--#endif-->
<!--#if (webViteReact)-->
The app itself is what Vite's official CLI, `create-vite`, writes for its `react-ts` template; this
folder adds the wiring around it.
<!--#endif-->
<!--#if (webViteVue)-->
The app itself is what Vite's official CLI, `create-vite`, writes for its `vue-ts` template; this
folder adds the wiring around it.
<!--#endif-->
<!--#if (webViteSvelte)-->
The app itself is what Vite's official CLI, `create-vite`, writes for its `svelte-ts` template; this
folder adds the wiring around it.
<!--#endif-->
<!--#if (webVue)-->
The app itself is what Vue's official CLI, `create-vue`, writes with TypeScript, Vue Router, Vitest
and ESLint; this folder adds the wiring around it.
<!--#endif-->
<!--#if (webReactRouter)-->
The app itself is what React Router's official CLI, `create-react-router`, writes from its default
template, made a single-page app; this folder adds the wiring around it.
<!--#endif-->
<!--#if (webAngular && !webServerRendering)-->
The app itself is what the Angular CLI's `ng new` writes (standalone components, routing, CSS,
no server-side rendering, Vitest); this folder adds the wiring around it.
<!--#endif-->
<!--#if (webAngular && webServerRendering)-->
The app itself is what the Angular CLI's `ng new --ssr` writes (standalone components, routing, CSS,
server-side rendering on its Express server, Vitest); this folder adds the wiring around it.
<!--#endif-->

## Create the app

The folder starts with the wiring only: scaffold.json names the CLI, the version it is pinned to
and its arguments, and overlay/ holds what is laid over the CLI's starter. From the project root:

```bash
dotnet run Tools/Tasks.cs web-scaffold FrameworkSite
```

`bootstrap` runs the same step. It needs the network once, to fetch the CLI. It writes the starter,
lays the overlay over it, merges the package settings of scaffold.json into its `package.json`,
pins every dependency to the version installed, removes scaffold.json and overlay/, then runs
`pnpm install` and `pnpm api` in `Src/Web`. Commit the app with `Src/Web/pnpm-lock.yaml`: from then
on `pnpm install --frozen-lockfile`, CI and the image build use exactly those versions, and the step
leaves the app alone.

<!--#if (webViteReact)-->
What the template adds to the CLI's starter, and nothing else:

- `vite.config.ts`, replaced whole, since a Vite dev server is configured there and nowhere else:
  the CLI's React plugin, port 5173, and `/api/` and `/health` relayed to the API (`API_UPSTREAM`,
  default <http://localhost:5187>), so the browser talks to a single origin and the API keeps CORS
  closed;
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, and a `test` script running Vitest beside the CLI's `dev`, `build`
  and `lint` (Oxlint), so `pnpm build`, `pnpm lint` and `pnpm test` in `Src/Web` include the app.
<!--#endif-->
<!--#if (webViteVue || webViteSvelte)-->
What the template adds to the CLI's starter, and nothing else:

- `vite.config.ts`, replaced whole, since a Vite dev server is configured there and nowhere else:
  the CLI's plugin, port 5173, and `/api/` and `/health` relayed to the API (`API_UPSTREAM`,
  default <http://localhost:5187>), so the browser talks to a single origin and the API keeps CORS
  closed;
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, and a `test` script running Vitest beside the CLI's `dev` and
  `build`, so `pnpm build` and `pnpm test` in `Src/Web` include the app. The CLI writes no linter,
  and the workspace's `pnpm lint` skips an app without a `lint` script.
<!--#endif-->
<!--#if (webViteSvelte)-->
- in tsconfig.app.json: `allowImportingTsExtensions`, since the workspace packages export their
  TypeScript sources and import each other by `.ts` path, and the CLI's `check` script type-checks
  them through the app; the CLI's svelte.config.js stays as it writes it.
<!--#endif-->
<!--#if (webVue)-->
What the template adds to the CLI's starter, and nothing else:

- `vite.config.ts`, replaced whole, since a Vite dev server is configured there and nowhere else:
  the CLI's Vue and Vue DevTools plugins and its `@` alias, kept as it writes them, plus port 5173
  and `/api/` and `/health` relayed to the API (`API_UPSTREAM`, default <http://localhost:5187>), so
  the browser talks to a single origin and the API keeps CORS closed;
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, a `test` script that runs Vitest once instead of watching, and the
  CLI's two lint scripts without `--fix`, so `pnpm lint`, `pnpm test` and `pnpm build` in `Src/Web`
  check the app without rewriting it; Vitest is pinned to the version the workspace's packages use.
<!--#endif-->
<!--#if (webReactRouter)-->
What the template adds to the CLI's starter, and nothing else:

- the CLI's default template, taken at a fixed commit of React Router's templates repository, since
  the CLI otherwise fetches that repository's latest files and its pinned version alone would not
  hold the starter still; Tailwind stays, as the template writes it;
- `react-router.config.ts`, replaced whole: `ssr: false`, a single-page app with no server, built
  into the dist folder; React Router writes the page and its assets in its client folder, which is
  what the image serves;
- `vite.config.ts`, replaced whole, since a Vite dev server is configured there and nowhere else:
  the CLI's Tailwind and React Router plugins and its path aliases, kept as it writes them, plus
  port 5173 and `/api/` and `/health` relayed to the API (`API_UPSTREAM`, default
  <http://localhost:5187>), so the browser talks to a single origin and the API keeps CORS closed;
- in `tsconfig.json`: `allowImportingTsExtensions`, since the workspace packages export their
  TypeScript sources and import each other by `.ts` path, and the CLI's `typecheck` script
  type-checks them through the app;
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, and a `test` script running Vitest beside the CLI's `dev`, `build`
  and `typecheck`, so `pnpm build` and `pnpm test` in `Src/Web` include the app; the CLI's `start`
  script and its `@react-router/serve` dependency are removed, since nginx serves the app. The CLI
  writes no linter, and the workspace's `pnpm lint` skips an app without a `lint` script;
- the CLI's own Dockerfile and .dockerignore are removed: the image is this folder's.

The page React Router builds starts the app from inline scripts, which the Content-Security-Policy
allows by their hash and no other way: when the image is built, the `Dockerfile` computes the
SHA-256 of each inline script in the built page and writes the server's configuration with those
hashes added to `script-src`. A change to the app that changes those scripts changes the hashes at
the next image build, with nothing to edit by hand. The starter's page links Google Fonts, which
the policy refuses like any other origin: serve the font from the app, or add its origins to
`style-src` and `font-src`.
<!--#endif-->
<!--#if (webAngular && !webServerRendering)-->
What the template adds to the CLI's starter, and nothing else; no file the CLI writes is replaced:

- `proxy.conf.json`, which `ng serve` reads through angular.json: `/api` and `/health` relayed to
  the API at <http://localhost:5187>, so the browser talks to a single origin and the API keeps CORS
  closed;
- in angular.json: the build output in the app's dist folder, that proxy, port 4200, and production
  styles without inlined critical CSS, whose `onload` handler the Content-Security-Policy refuses;
- in `tsconfig.json`: `allowImportingTsExtensions` with `noEmit`, since the workspace packages export
  their TypeScript sources and import each other by `.ts` path (the Angular builder emits through its
  own bundler either way);
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, a `dev` script and a `test` script that runs once instead of
  watching, so `pnpm build` and `pnpm test` in `Src/Web` include the app. The CLI writes no linter;
  `ng add angular-eslint` adds one and its `lint` script.
<!--#endif-->
<!--#if (webAngular && webServerRendering)-->
What the template adds to the CLI's starter, and nothing else:

- `src/server.ts`, the CLI's Express server, replaced whole since a server is configured nowhere
  else: it relays `/api/` and `/health` to the API (`API_UPSTREAM`, default
  <http://localhost:5187>) through `relayToApi` of `@app-template/api-client/server`, so the
  browser talks to a single origin and the API keeps CORS closed; it sends the page security
  headers, caches a file whose name carries a content hash for a year and revalidates every other,
  and renders each page with a fresh nonce, which its Content-Security-Policy names in `script-src`
  and its `Cache-Control: no-store` keeps from being served twice; with local accounts its relay is
  `serverSessions`, which keeps each browser's session as "Calling the API and signing in" below
  describes. The build writes Angular's
  event-dispatch contract inline into the page template, without a nonce, so the server adds to
  `script-src` the SHA-256 of each inline script of that template, computed when it starts;
- `src/app/app.config.server.ts`, the CLI's, plus `CSP_NONCE` read from the request context, so
  Angular writes the response's nonce on each script it adds while rendering;
- `src/app/app.routes.server.ts`: every route rendered for each request instead of prerendered at
  build, since a prerendered page cannot carry a response's nonce;
- `proxy.conf.json`, which `ng serve` reads through angular.json: `/api` and `/health` relayed to
  the API at <http://localhost:5187>;
- in angular.json: the build output in the app's dist folder (`dist/browser` and `dist/server`),
  that proxy, port 4200, and production styles without inlined critical CSS, whose loader script
  carries no nonce;
- in `tsconfig.json`: `allowImportingTsExtensions` with `noEmit`, since the workspace packages export
  their TypeScript sources and import each other by `.ts` path;
- in `package.json`: the name `framework-site`, the dependency `@app-template/api-client`
  (`workspace:*`), a `dev` script, a `test` script that runs once instead of watching, and `start`,
  which runs the built server, in place of the CLI's `serve:ssr` script. The CLI writes no linter;
  `ng add angular-eslint` adds one and its `lint` script.

The server checks each request's host against `NG_ALLOWED_HOSTS`, Angular's defence against
server-side request forgery, and answers `400` to any other: the image allows `localhost` and
`127.0.0.1`, and a deployment sets the host its users reach.
<!--#endif-->

## Calling the API and signing in

The pages are the CLI's starter: the screens are yours to write. The client is typed from the API's
OpenAPI document, and `pnpm api` in `Src/Web` regenerates it when the API changes.
<!--#if (localAuth)-->
<!--#if (!webServerRendering)-->

A browser keeps the access token in memory and leaves the refresh token to an `HttpOnly` cookie the
API sets in cookie mode. `createBrowserSession` holds the token, `sessionMiddleware` sends it,
asks for cookie mode on the sign-in paths and refreshes once on a `401`; call `refresh()` at start,
since a reload loses the token and the cookie brings it back:

```ts
import { createApiClient, createBrowserSession, sessionMiddleware, unwrap } from '@app-template/api-client';

export const session = createBrowserSession();
export const api = createApiClient({ middleware: [sessionMiddleware(session)] });

await session.refresh();

const answer = await unwrap(api.POST('/api/v1/auth/login', { body: { email, password } }));

if (answer.status === 'twoFactorRequired') {
  // ask for the code, then POST /api/v1/auth/login/two-factor with answer.challengeToken
} else {
  session.signedIn(answer.tokens);
}

const me = await unwrap(api.GET('/api/v1/auth/me'));
```

`unwrap` throws an `ApiError` whose `fieldErrors` are the problem document's `errors`, ready to show
beside the form's fields; `session.signOut()` ends the session and clears the cookie.
<!--#endif-->
<!--#if (webServerRendering)-->

This app's server keeps the session: `serverSessions` of `@app-template/api-client/server` holds
both tokens in the server's memory, and the browser only the `__Host-session` cookie it sets
(`HttpOnly`, `Secure`, `SameSite=Lax`). The page posts the sign-in routes through the relay, which
keeps the tokens and answers without them, then calls the API with no token, the server adding it.
`serverSessionMiddleware` sends the `X-CSRF: 1` header the server requires on a call that changes
state:

```ts
import { createApiClient, serverSessionMiddleware, unwrap } from '@app-template/api-client';

export const api = createApiClient({ middleware: [serverSessionMiddleware()] });

const answer = await unwrap(api.POST('/api/v1/auth/login', { body: { email, password } }));

if (answer.status === 'twoFactorRequired') {
  // ask for the code, then POST /api/v1/auth/login/two-factor with answer.challengeToken
}

const me = await unwrap(api.GET('/api/v1/auth/me'));
```

A page rendered on the server calls the API from there, with the token of the browser's session:
`src/server.ts` hands Angular `apiUpstream` and `accessToken` in the request context, which
`inject(REQUEST_CONTEXT)` reads. `POST /api/v1/auth/logout` through the relay revokes the session
and clears the cookie; `unwrap` throws an `ApiError` whose `fieldErrors` are the problem document's
`errors`.
<!--#endif-->
<!--#if (adminAudience)-->
The account administration endpoints, `/api/v1/auth/accounts` first, answer `403` to an account
without the `Admin` role, which `me.roles` shows before the screens ask.
<!--#endif-->
<!--#elseif (externalAuth)-->

The API accepts the identity provider's access tokens. Sign in with a maintained OpenID Connect
library running the Authorization Code flow with PKCE, keep its tokens in memory, and hand the
current access token to the client, which sends it on every call:

```ts
import { createApiClient } from '@app-template/api-client';

export const api = createApiClient({ accessToken: () => currentAccessToken() });
```

The provider's origin then joins `connect-src` in the Content-Security-Policy of `nginx.conf`.
<!--#else-->

The API has no accounts, so every endpoint answers without a sign-in:

```ts
import { createApiClient } from '@app-template/api-client';

export const api = createApiClient();
```
<!--#endif-->

<!--#if (localAuth)-->

### The pages the mailed links open

Confirmation, password-reset and email-change mails link to pages this app does not have yet. Add
`/confirm-email`, `/reset-password` and `/confirm-email-change` as the contract in `docs/features/accounts.md`
("The pages the mailed links open") describes, then point `EmailConfirmation:ConfirmEmailUrl`,
`PasswordReset:ResetPasswordUrl` and `EmailChange:ConfirmEmailChangeUrl` at this app's origin.
<!--#endif-->

## Run it

With the API running (`dotnet run Tools/Tasks.cs run` at the project root), from `Src/Web`:

```bash
pnpm --filter framework-site dev
```

<!--#if (!webServerRendering)-->
Under Compose, from the project root with the stack's `.env` in place, `docker compose up --build
--wait` serves the build through nginx on <http://localhost:5173>, or on the port
`WEB_FRAMEWORK_SITE_HOST_PORT` sets.
<!--#endif-->
<!--#if (webServerRendering)-->
Under Compose, from the project root with the stack's `.env` in place, `docker compose up --build
--wait` runs the built server on Node at <http://localhost:5173>, or on the port
`WEB_FRAMEWORK_SITE_HOST_PORT` sets.
<!--#endif-->

| File | What it is |
|---|---|
<!--#if (!webAngular && !webReactRouter)-->
| `nginx.conf` | the server: the page security headers, a strict Content-Security-Policy among them, caching, the `index.html` fallback, the `/api/` and `/health` relay |
<!--#endif-->
<!--#if (webReactRouter)-->
| `nginx.conf` | the server: the page security headers, a strict Content-Security-Policy among them, caching, the `index.html` fallback, the `/api/` and `/health` relay; the image build adds the hashes of the page's inline scripts to its `script-src` |
<!--#endif-->
<!--#if (webAngular && !webServerRendering)-->
| `nginx.conf` | the server: the page security headers, caching, the `index.html` fallback, the `/api/` and `/health` relay; its Content-Security-Policy takes scripts from the app's origin only, and styles from it or inline, since Angular inserts each component's styles as a `<style>` element when the page runs |
<!--#endif-->
<!--#if (!webServerRendering)-->
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile |
<!--#endif-->
<!--#if (webServerRendering)-->
| `src/server.ts` | the server: the page security headers, caching, the `/api/` and `/health` relay, and the page rendered with a nonce its Content-Security-Policy names; styles come from the app's origin or inline, since Angular inserts each component's styles as a `<style>` element |
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile, running the built server on Node as an unprivileged user |
<!--#endif-->
| `compose.yml` | the `web-framework-site` service |

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it, under `include:`:

```yaml
  - path: Src/Web/apps/FrameworkSite/compose.yml
```

and `.env.example` names its port:

```bash
WEB_FRAMEWORK_SITE_HOST_PORT=5173
```

Removing the app is deleting this folder and those two lines, then running `pnpm install` in
`Src/Web`.
