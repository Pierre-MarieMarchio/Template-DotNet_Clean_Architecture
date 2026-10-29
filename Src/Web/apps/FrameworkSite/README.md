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
<!--#if (webReactRouter && !webServerRendering)-->
The app itself is what React Router's official CLI, `create-react-router`, writes from its default
template, made a single-page app; this folder adds the wiring around it.
<!--#endif-->
<!--#if (webReactRouter && webServerRendering)-->
The app itself is what React Router's official CLI, `create-react-router`, writes from its
`node-custom-server` template (server-side rendering on its Express server, Tailwind); this folder
adds the wiring around it.
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
<!--#if (webReactRouter && !webServerRendering)-->
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
<!--#if (webReactRouter && webServerRendering)-->
What the template adds to the CLI's starter, and nothing else:

- the CLI's `node-custom-server` template, taken at a fixed commit of React Router's templates
  repository, since the CLI otherwise fetches that repository's latest files and its pinned version
  alone would not hold the starter still; Tailwind stays, as the template writes it;
- `server/app.ts`, the CLI's Express app, replaced whole since a server is configured nowhere else:
  it relays `/api/` and `/health` to the API (`API_UPSTREAM`, default <http://localhost:5187>)
  through `relayToApi` of `@app-template/api-client/server`, so the browser talks to a single origin
  and the API keeps CORS closed; it sends the page security headers, caches a file whose name
  carries a content hash for a year and revalidates every other, and renders each page with a fresh
  nonce, which its Content-Security-Policy names in `script-src` and its `Cache-Control: no-store`
  keeps from being served twice; with local accounts its relay is `serverSessions`, and with an
  external provider `providerSessions`, which signs people in at the provider, each keeping the
  browser's session as "Calling the API and signing in" below describes. It hands React Router
  `renderContext` in the load context: the nonce and the API's address, with the session's token
  under accounts. The build bundles it, with every dependency, into `dist/server/server.mjs`, which
  listens on `PORT` when Node runs it;
- `app/entry.server.tsx`, React Router's default server entry plus the response's nonce, which
  `<ServerRouter nonce>` writes on each script and link React Router renders, and React on the
  scripts it streams;
- `app/context.ts`, the CLI's, plus `renderContext`;
- `server.js`, the CLI's server, replaced by the development server alone: Vite in middleware mode,
  its hot-reload socket on the same port, and `server/app.ts` loaded for each request, so
  `pnpm dev` relays `/api/` and `/health` through the server's relay, the session included, on port
  5173 (`PORT`). In development the policy also takes inline styles, which Vite inserts as
  `<style>` elements;
- `react-router.config.ts`, replaced whole: `ssr: true`, built into the dist folder, the server's
  entry named `server.mjs`;
- `vite.config.ts`, replaced whole: the CLI's Tailwind and React Router plugins, its path aliases and
  its server entry, plus every dependency bundled into the server build, so the image holds the
  build output alone;
- in `tsconfig.json`: `allowImportingTsExtensions`, since the workspace packages export their
  TypeScript sources and import each other by `.ts` path, and the CLI's `typecheck` script
  type-checks them through the app;
- in `package.json`: the name `framework-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), `start`, which runs the built server, and a `test`
  script running Vitest beside the CLI's `dev`, `build` and `typecheck`; the CLI's `compression`
  and `morgan` and their types are removed, since `server/app.ts` uses neither. The CLI writes no
  linter, and the workspace's `pnpm lint` skips an app without a `lint` script;
- the CLI's own Dockerfile and .dockerignore are removed: the image is this folder's.

The starter's page links Google Fonts, which the policy refuses like any other origin: serve the font
from the app, or add its origins to `style-src` and `font-src` in `server/app.ts`.
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
  `serverSessions`, and with an external provider `providerSessions`, which signs people in at the
  provider, each keeping the browser's session as "Calling the API and signing in" below
  describes. The build writes Angular's
  event-dispatch contract inline into the page template, without a nonce, so the server adds to
  `script-src` the SHA-256 of each inline script of that template, computed when it starts;
- `src/app/app.config.server.ts`, the CLI's, plus `CSP_NONCE` read from the request context, so
  Angular writes the response's nonce on each script it adds while rendering;
- `src/app/app.routes.server.ts`: every route rendered for each request instead of prerendered at
  build, since a prerendered page cannot carry a response's nonce;
- in angular.json: the build output in the app's dist folder (`dist/browser` and `dist/server`),
  port 4200, and production styles without inlined critical CSS, whose loader script carries no
  nonce. `ng serve` runs `src/server.ts` too, so `pnpm dev` relays `/api` and `/health` through the
  server's relay, the session included, with no proxy of its own;
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

<!--#if (webAngular)-->
A page rendered on the server calls the API from there, with the token of the browser's session:
`src/server.ts` hands Angular `apiUpstream` and `accessToken` in the request context, which
`inject(REQUEST_CONTEXT)` reads.
<!--#else-->
A page rendered on the server calls the API from there, with the token of the browser's session:
`server/app.ts` hands React Router `renderContext` in the load context, whose `apiUpstream` and
`accessToken` a loader reads:

```ts
import { createApiClient, unwrap } from '@app-template/api-client';
import { renderContext } from '~/context';

export async function loader({ context }: Route.LoaderArgs) {
  const { apiUpstream, accessToken } = context.get(renderContext);
  const api = createApiClient({ baseUrl: apiUpstream, accessToken });

  return { me: await unwrap(api.GET('/api/v1/auth/me')) };
}
```
<!--#endif-->

`POST /api/v1/auth/logout` through the relay revokes the session and clears the cookie; `unwrap`
throws an `ApiError` whose `fieldErrors` are the problem document's `errors`.
<!--#endif-->
<!--#if (adminAudience)-->
The account administration endpoints, `/api/v1/auth/accounts` first, answer `403` to an account
without the `Admin` role, which `me.roles` shows before the screens ask.
<!--#endif-->
<!--#elseif (externalAuth)-->
<!--#if (!webServerRendering)-->

The API accepts the identity provider's access tokens. Sign in with a maintained OpenID Connect
library running the Authorization Code flow with PKCE, keep its tokens in memory, and hand the
current access token to the client, which sends it on every call:

```ts
import { createApiClient } from '@app-template/api-client';

export const api = createApiClient({ accessToken: () => currentAccessToken() });
```

The provider's origin then joins `connect-src` in the Content-Security-Policy of `nginx.conf`.
<!--#endif-->
<!--#if (webServerRendering)-->

This app's server signs people in at the provider and keeps the session: `providerSessions` of
`@app-template/api-client/server` runs the authorization code flow with PKCE as the provider's
client, holds the provider's tokens in the server's memory, and gives the browser only the
`__Host-session` cookie it sets (`HttpOnly`, `Secure`, `SameSite=Lax`). The page signs in by
navigating to `/session/sign-in`, calls the API with no token, the server adding it, and signs out
by posting to `/session/sign-out`, which answers where to send the browser next.
`serverSessionMiddleware` sends the `X-CSRF: 1` header the server requires on a call that changes
state:

```ts
import { createApiClient, serverSessionMiddleware } from '@app-template/api-client';

export const api = createApiClient({ middleware: [serverSessionMiddleware()] });

location.assign(`/session/sign-in?returnTo=${encodeURIComponent(location.pathname)}`);

const answer = await fetch('/session/sign-out', { method: 'POST', headers: { 'X-CSRF': '1' } });
location.assign(((await answer.json()) as { location: string }).location);
```

`/session/sign-in` keeps a code verifier, a state and a nonce for ten minutes under the
`__Host-sign-in` cookie and sends the browser to the provider; `/session/callback` accepts the state
it sent, once, redeems the code, checks the identity token's nonce, opens the session and returns to
`returnTo`, a path of this origin, or `/`. Sign in by a link or a navigation: the policy's
`form-action 'self'` refuses a form whose answer redirects to the provider. `/session/sign-out`
forgets the tokens and answers `{ "location": … }`: the provider's `end_session_endpoint`, with the
identity token and `<origin>/` to come back to, when its discovery document names one, which ends
the provider's session too; `/` otherwise, which leaves it open, so the next sign-in may not ask for
a password. The server refreshes the access token 30 seconds before it expires when the provider
issued a refresh token, once for all the requests of a session; without one, the session ends with
the access token. A session lasts twelve hours from its sign-in at most, and a restart of the server
ends them all.

<!--#if (webAngular)-->
A page rendered on the server calls the API from there, with the token of the browser's session:
`src/server.ts` hands Angular `apiUpstream` and `accessToken` in the request context, which
`inject(REQUEST_CONTEXT)` reads.
<!--#else-->
A page rendered on the server calls the API from there, with the token of the browser's session:
`server/app.ts` hands React Router `renderContext` in the load context, whose `apiUpstream` and
`accessToken` a loader passes to `createApiClient({ baseUrl: apiUpstream, accessToken })`.
<!--#endif-->

Register the app at the provider before its first sign-in:

- a client allowed the authorization code flow with PKCE: confidential, with a secret, since the
  server keeps it, or public, without one;
- for each origin that serves the app, the redirect URI `<origin>/session/callback` and the
  post-logout redirect URI `<origin>/`: `http://localhost` on the port of
  `WEB_FRAMEWORK_SITE_HOST_PORT` through the stack, the `PUBLIC_ORIGIN` of `pnpm dev` that "Run it"
  gives, and the public one in a deployment;
- the scopes of `EXTERNAL_AUTH_INTERACTIVE_SCOPE`, `openid profile email offline_access` by
  default, with refresh tokens allowed for `offline_access`;
- the API's audience, `Authentication:External:Audience`, in the access token it issues. How depends
  on the provider: an API scope the client requests, added to `EXTERNAL_AUTH_INTERACTIVE_SCOPE`; an
  audience mapper on the client; or a parameter of the authorization request, such as Auth0's
  `audience` or the `resource` of RFC 8707, which the server passes as
  `providerSessions(apiUpstream, process.env, { authorizationParameters: { audience: '<api>' } })`.

Then set, in `.env`, `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID`, the secret of a confidential client in
`EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET`, and `WEB_FRAMEWORK_SITE_ORIGIN`, the origin browsers reach
the app at; `EXTERNAL_AUTH_AUTHORITY` is the issuer the API trusts. `compose.yml` passes them to the
server, the origin as `PUBLIC_ORIGIN`, and the server refuses to start until each is set, naming
those that are not, through `checkProviderSettings`. The build and `pnpm dev` load the server
without them, and a sign-in then fails until they are set. The server reads the provider's discovery
document at the first sign-in, over `https` only, and answers `502` `session.providerUnreachable`
while the provider cannot be reached.
<!--#endif-->
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

<!--#if (webAngular && webServerRendering && externalAuth)-->
A sign-in under `pnpm dev` needs `EXTERNAL_AUTH_AUTHORITY`, `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID` and
`PUBLIC_ORIGIN=http://localhost:4200` in the shell that runs it, since no Compose file sets them there.

<!--#endif-->
<!--#if (webReactRouter && webServerRendering && externalAuth)-->
A sign-in under `pnpm dev` needs `EXTERNAL_AUTH_AUTHORITY`, `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID` and
`PUBLIC_ORIGIN=http://localhost:5173` in the shell that runs it, since no Compose file sets them there.

<!--#endif-->
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
<!--#if (webReactRouter && !webServerRendering)-->
| `nginx.conf` | the server: the page security headers, a strict Content-Security-Policy among them, caching, the `index.html` fallback, the `/api/` and `/health` relay; the image build adds the hashes of the page's inline scripts to its `script-src` |
<!--#endif-->
<!--#if (webAngular && !webServerRendering)-->
| `nginx.conf` | the server: the page security headers, caching, the `index.html` fallback, the `/api/` and `/health` relay; its Content-Security-Policy takes scripts from the app's origin only, and styles from it or inline, since Angular inserts each component's styles as a `<style>` element when the page runs |
<!--#endif-->
<!--#if (!webServerRendering)-->
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile |
<!--#endif-->
<!--#if (webAngular && webServerRendering)-->
| `src/server.ts` | the server: the page security headers, caching, the `/api/` and `/health` relay, and the page rendered with a nonce its Content-Security-Policy names; styles come from the app's origin or inline, since Angular inserts each component's styles as a `<style>` element |
<!--#endif-->
<!--#if (webReactRouter && webServerRendering)-->
| `server/app.ts` | the server: the page security headers, caching, the `/api/` and `/health` relay, and the page rendered with a nonce its strict Content-Security-Policy names |
| `server.js` | the development server `pnpm dev` runs: Vite in middleware mode in front of `server/app.ts` |
<!--#endif-->
<!--#if (webServerRendering)-->
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile, running the built server on Node as an unprivileged user |
<!--#endif-->
| `compose.yml` | the `web-framework-site` service |

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it, under `include:`:

```yaml
  - path: Src/Web/apps/FrameworkSite/compose.yml
```

<!--#if (webServerRendering && externalAuth)-->
and `.env.example` names its port and its origin:

```bash
WEB_FRAMEWORK_SITE_HOST_PORT=5173
# The origin browsers reach the server-rendered app at, its redirect URI's origin at the provider: change it with the port.
WEB_FRAMEWORK_SITE_ORIGIN=http://localhost:5173
```

A project whose `.env.example` names no `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID` yet takes these lines
after `EXTERNAL_AUTH_ADMINISTRATOR_ROLE=Admin`:

```bash
# The server-rendered web app's client at the provider, which signs people in with the code flow; a public client leaves the secret empty.
EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID=
EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET=
```

Removing the app is deleting this folder, its `include:` line and its lines of `.env.example`, then
running `pnpm install` in `Src/Web`.
<!--#else-->
and `.env.example` names its port:

```bash
WEB_FRAMEWORK_SITE_HOST_PORT=5173
```

Removing the app is deleting this folder and those two lines, then running `pnpm install` in
`Src/Web`.
<!--#endif-->
