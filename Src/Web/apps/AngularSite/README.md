# AngularSite

<!--#if (adminAudience)-->
An Angular back office over the API, for administrators.
<!--#else-->
An Angular front end over the API.
<!--#endif-->
The app itself is what the Angular CLI's `ng new` writes (standalone components, routing, CSS,
no server-side rendering, Vitest); this folder adds the wiring around it.

## Create the app

The folder starts with the wiring only: scaffold.json names the CLI, the version it is pinned to
and its arguments, and overlay/ holds what is laid over the CLI's starter. From the project root:

```bash
dotnet run Tools/Tasks.cs web-scaffold AngularSite
```

`bootstrap` runs the same step. It needs the network once, to fetch the CLI. It writes the starter,
lays the overlay over it, merges the package settings of scaffold.json into its `package.json`,
pins every dependency to the version installed, removes scaffold.json and overlay/, then runs
`pnpm install` and `pnpm api` in `Src/Web`. Commit the app with `Src/Web/pnpm-lock.yaml`: from then
on `pnpm install --frozen-lockfile`, CI and the image build use exactly those versions, and the step
leaves the app alone.

What the template adds to the CLI's starter, and nothing else; no file the CLI writes is replaced:

- `proxy.conf.json`, which `ng serve` reads through angular.json: `/api` and `/health` relayed to
  the API at <http://localhost:5187>, so the browser talks to a single origin and the API keeps CORS
  closed;
- in angular.json: that proxy, port 4200, and production styles without inlined critical CSS,
  whose `onload` handler the Content-Security-Policy refuses;
- in `tsconfig.json`: `allowImportingTsExtensions` with `noEmit`, since the workspace packages export
  their TypeScript sources and import each other by `.ts` path (the Angular builder emits through its
  own bundler either way);
- in `package.json`: the name `angular-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, a `dev` script and a `test` script that runs once instead of
  watching, so `pnpm build` and `pnpm test` in `Src/Web` include the app. The CLI writes no linter;
  `ng add angular-eslint` adds one and its `lint` script.

## Calling the API and signing in

The pages are the CLI's starter: the screens are yours to write. The client is typed from the API's
OpenAPI document, and `pnpm api` in `Src/Web` regenerates it when the API changes.
<!--#if (localAuth)-->

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
pnpm --filter angular-site dev
```

Under Compose, from the project root with the stack's `.env` in place, `docker compose up --build
--wait` serves the build through nginx on <http://localhost:4200>, or on the port
`WEB_ANGULAR_SITE_HOST_PORT` sets.

| File | What it is |
|---|---|
| `nginx.conf` | the server: the page security headers, caching, the `index.html` fallback, the `/api/` and `/health` relay; its Content-Security-Policy takes scripts from the app's origin only, and styles from it or inline, since Angular inserts each component's styles as a `<style>` element when the page runs |
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile |
| `compose.yml` | the `web-angular-site` service |

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it, under `include:`:

```yaml
  - path: Src/Web/apps/AngularSite/compose.yml
```

and `.env.example` names its port:

```bash
WEB_ANGULAR_SITE_HOST_PORT=4200
```

Removing the app is deleting this folder and those two lines, then running `pnpm install` in
`Src/Web`.
