# ReactSite

<!--#if (adminAudience)-->
A React back office over the API, for administrators.
<!--#else-->
A React front end over the API.
<!--#endif-->
The app itself is what Vite's official CLI, `create-vite`, writes for its `react-ts` template; this
folder adds the wiring around it.

## Create the app

The folder starts with the wiring only: `scaffold.json` names the CLI, the version it is pinned to
and its arguments, and `overlay/` holds what is laid over the CLI's starter. From the project root:

```bash
dotnet run Tools/Tasks.cs web-scaffold ReactSite
```

`bootstrap` runs the same step. It needs the network once, to fetch the CLI. It writes the starter,
lays the overlay over it, merges the package settings of `scaffold.json` into its `package.json`,
pins every dependency to the version installed, removes `scaffold.json` and `overlay/`, then runs
`pnpm install` and `pnpm api` in `Src/Web`. Commit the app with `Src/Web/pnpm-lock.yaml`: from then
on `pnpm install --frozen-lockfile`, CI and the image build use exactly those versions, and the step
leaves the app alone.

What the template adds to the CLI's starter, and nothing else:

- `vite.config.ts`, replaced whole, since a Vite dev server is configured there and nowhere else:
  the CLI's React plugin, port 5173, and `/api/` and `/health` relayed to the API (`API_UPSTREAM`,
  default <http://localhost:5187>), so the browser talks to a single origin and the API keeps CORS
  closed;
- in `package.json`: the name `react-site`, which `pnpm --filter` takes, the dependency
  `@app-template/api-client` (`workspace:*`), the client generated from the API in
  `Src/Web/packages/api-client`, and a `test` script running Vitest beside the CLI's `dev`, `build`
  and `lint` (Oxlint), so `pnpm build`, `pnpm lint` and `pnpm test` in `Src/Web` include the app.

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

if ('challengeToken' in answer) {
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
`/confirm-email`, `/reset-password` and `/confirm-email-change` as the contract in `docs/CONFIGURATION.md`
("The pages the mailed links open") describes, then point `EmailConfirmation:ConfirmEmailUrl`,
`PasswordReset:ResetPasswordUrl` and `EmailChange:ConfirmEmailChangeUrl` at this app's origin.
<!--#endif-->

## Run it

With the API running (`dotnet run Tools/Tasks.cs run` at the project root), from `Src/Web`:

```bash
pnpm --filter react-site dev
```

Under Compose, from the project root with the stack's `.env` in place, `docker compose up --build
--wait` serves the build through nginx on <http://localhost:5173>, or on the port
`WEB_REACT_SITE_HOST_PORT` sets.

| File | What it is |
|---|---|
| `nginx.conf` | the server: the page security headers, a strict Content-Security-Policy among them, caching, the `index.html` fallback, the `/api/` and `/health` relay |
| `Dockerfile` | the image, built from `Src/Web` with the workspace lockfile |
| `compose.yml` | the `web-react-site` service |

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it, under `include:`:

```yaml
  - path: Src/Web/apps/ReactSite/compose.yml
```

and `.env.example` names its port:

```bash
WEB_REACT_SITE_HOST_PORT=5173
```

Removing the app is deleting this folder and those two lines, then running `pnpm install` in
`Src/Web`.
