# Web front end

How web apps run beside the API in this project, call it, and sign their users in.

## What you get

`Src/Web` is one pnpm workspace with one lockfile. Node comes from `Src/Web/.node-version`, pnpm
from the `packageManager` field of `Src/Web/package.json`, which Corepack reads; run pnpm from
`Src/Web`. Nothing in the .NET build needs Node.

| Path | What it is |
|---|---|
| `Src/Web/apps/<Name>` | one app: its sources, `Dockerfile`, `nginx.conf`, `compose.yml` and `README.md` |
| `Src/Web/packages/api-client` | the TypeScript client generated from the API's OpenAPI document |
| `Src/Web/packages/tsconfig`, `Src/Web/packages/eslint-config` | the shared compiler and lint settings |

Each app is its own deployable. Its development server, and in Compose its nginx container, relays
`/api/` and `/health` to the API, so the browser talks to one origin per app and the API keeps
CORS closed.

<!--#if (webFramework)-->
## Creating the app

The app is written by its framework's official CLI, `create-vite` with the `react-ts` template or
the Angular CLI's `ng new`, at a version pinned in the app's folder. `bootstrap` runs that step
once, through `dotnet run Tools/Tasks.cs web-scaffold`, and needs the network to fetch the CLI.
It writes the CLI's starter, lays the wiring over it (the `/api` relay, the package name, the
`api-client` dependency, the `dev`, `build`, `lint` and `test` scripts), pins every dependency to
the version installed, then runs `pnpm install` and `pnpm api`.

The CLI runs with its input closed, from the scratch folder it writes into. A CLI that asks a
question therefore makes `web-scaffold` fail, leaving the app's folder unchanged, instead of waiting
for an answer: the descriptor in the app's folder gives the CLI every answer in its arguments.

Commit the app with the lockfile. From then on the app is this project's own: `web-scaffold` leaves
it alone, and the pages are yours to write.
<!--#endif-->
<!--#if (webPlaceholder)-->
## Creating the app

The app is a static page in `public/`, served by nginx: replace those files with the build output
of the front end this project grows into.
<!--#endif-->

## Running it

With the API running on the host (`dotnet run Tools/Tasks.cs run`), from `Src/Web`:

```bash
pnpm --filter <app> dev
```

`<app>` is the folder's kebab name. The development server relays to `API_UPSTREAM`, by default
the API on the host. Under Compose, `docker compose up --build --wait` serves each app's build
through nginx on `127.0.0.1`, on the port its `WEB_<NAME>_HOST_PORT` sets in `.env`. `pnpm build`,
`pnpm lint` and `pnpm test` in `Src/Web` run every app and package.

## Calling the API

The client is `openapi-fetch` over the types generated from the API's own document, so a route, a
body or a response that does not exist fails the type check.

<!--#if (notes)-->
```ts
import { ApiError, createApiClient, unwrap } from '@app-template/api-client';

const api = createApiClient();

try {
  const note = await unwrap(api.POST('/api/v1/notes', { body: { title, body } }));
} catch (error) {
  if (error instanceof ApiError && error.code === 'request.validationFailed') {
    showErrors(error.fieldErrors);
  }
}
```
<!--#endif-->
<!--#if (!notes)-->
```ts
import { ApiError, createApiClient, unwrap } from '@app-template/api-client';

const api = createApiClient();

try {
  const created = await unwrap(api.POST('/api/v1/<resource>', { body }));
} catch (error) {
  if (error instanceof ApiError && error.code === 'request.validationFailed') {
    showErrors(error.fieldErrors);
  }
}
```
<!--#endif-->

`unwrap` returns the body or throws an `ApiError` carrying the problem document's `code` and field
`errors`; an answer that is no problem document becomes `response.unreadable`.

After any change to the API's contract, run `pnpm api` in `Src/Web`: it builds the API, writes its
OpenAPI document and regenerates `Src/Web/packages/api-client/src/generated/schema.ts`, which is
committed so an app type-checks without the .NET SDK
([ADR 0019](../decisions/0019-one-generated-api-client.md)).

## Signing in

<!--#if (localAuth)-->
### Local accounts

A browser keeps the access token in a variable and leaves the refresh token to the `HttpOnly`
cookie the API sets in cookie mode ([Authentication](authentication.md#browsers-the-refresh-token-cookie)).
`createBrowserSession` holds the token; `sessionMiddleware` sends it, asks for cookie mode on the
sign-in, refresh and logout routes, and on a `401` refreshes once and replays the call. Call
`refresh()` at start: a reload loses the variable, and the cookie brings the token back.

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
```

`session.signOut()` ends the session and clears the cookie; `session.onChange` reports when the
signed-in state flips. Concurrent refreshes share one request, since the API treats a second
presentation of a cookie as theft; only a refused refresh signs the page out.
<!--#endif-->
<!--#if (externalAuth)-->
### External provider

Sign in with a maintained OpenID Connect library running the Authorization Code flow with PKCE,
keep its tokens in memory, and hand the current access token to the client, which sends it on every
call:

```ts
import { createApiClient } from '@app-template/api-client';

export const api = createApiClient({ accessToken: () => currentAccessToken() });
```

Add the provider's origin to `connect-src` in the app's `nginx.conf`. The session module of
`api-client` is unused in this mode.
<!--#endif-->
<!--#if (publicApi)-->
### No accounts

The API authenticates nobody, so `createApiClient()` with no option calls every route.
<!--#endif-->

<!--#if (localAuth)-->
## The pages the mailed links open

The confirmation, password-reset and email-change mails link to `/confirm-email`,
`/reset-password` and `/confirm-email-change`. The starter app has none of these pages, so the
links keep naming the API's origin. To move them to an app:

1. Add the three pages, each following the contract in
   [Accounts](accounts.md#the-pages-the-mailed-links-open): read the fragment, drop it, post it.
2. Point `EmailConfirmation:ConfirmEmailUrl`, `PasswordReset:ResetPasswordUrl` and
   `EmailChange:ConfirmEmailChangeUrl` at the app: its development server's origin in
   `appsettings.Development.json`, `CONFIRM_EMAIL_URL`, `RESET_PASSWORD_URL` and
   `CONFIRM_EMAIL_CHANGE_URL` in `.env` under Compose, its public origin in a deployment.

The app's relay forwards each page's `POST /api/...` to the API.
<!--#endif-->

<!--#if (accounts)-->
## A back office

An app generated for the `admin` audience is meant for administrators. The account administration
routes answer `403` to an account without the administrator role, so read the caller's roles
from `GET /api/v1/auth/me` before showing those screens. An admin app needs accounts: a public API
has no administrator to sign in.

<!--#endif-->
## More apps and packages

Add an app at the project root; it gets its own folder, image and Compose service:

```bash
dotnet new cleanarch-webapp -n BackOffice --framework angular --audience admin --scope app-template --port 8082
```

`--framework` is `react`, `angular` or `placeholder`; `--scope` is the package scope of
`Src/Web/package.json`; `--auth` repeats this project's value. The command prints
two lines to add, also in the app's `README.md`: an `include:` entry in `docker-compose.yml` and
`WEB_<NAME>_HOST_PORT` in `.env.example`. Then run
`dotnet run Tools/Tasks.cs web-scaffold BackOffice` for a framework app, or `pnpm install` for a
placeholder, and commit the app with the lockfile. CI and Dependabot find apps by folder.

Add a framework-agnostic package, one exported function and its test, the same way:

```bash
dotnet new cleanarch-webpackage -n tokens --scope app-template
```

Run `pnpm install` in `Src/Web` and add `"@app-template/tokens": "workspace:*"` to each app that
uses it. Internal packages are private and export their TypeScript sources, which each app's bundler
compiles. An app never imports another app. A React or Angular package is the project's to write.

Removing an app is deleting its folder, its `include:` line and its port line, then running
`pnpm install`.

A project generated without a web app adds its first one with the same command plus
`--workspace` (`--scope` required), which also creates `Src/Web` and `.github/workflows/web.yml`;
the API side is in every project already. Add the lines `Src/Web/README.md` lists, then create the
app as above.

## Images and headers

Each app is its own image, built from `Src/Web` with the workspace lockfile and served by
`nginx-unprivileged` (uid 101, port 8080); `API_UPSTREAM` names the API, resolved per request.
Build one alone from the project root:

```bash
docker build -f Src/Web/apps/<Name>/Dockerfile Src/Web
```

`nginx.conf` sets the page headers on the files it serves: a strict Content-Security-Policy,
`X-Content-Type-Options`, `Referrer-Policy`, `Cross-Origin-Opener-Policy` and `Cache-Control`.
Relayed `/api/` and `/health` answers keep the API's own headers. A file whose name carries a
content hash is cached for a year; every other file, `index.html` first, revalidates, and an
unknown path falls back to `index.html` for the app's router.
<!--#if (webAngular)-->
The Angular app's policy allows inline styles, which Angular inserts at run time, and never inline
scripts.
<!--#endif-->

<!--#if (ci)-->
## CI

The web job installs with the frozen lockfile, lints, tests and builds every app and package, runs
`pnpm api` and fails when the generated client differs from the committed one. An image job builds
each app under `Src/Web/apps` holding a `Dockerfile` and a `package.json`, and checks that it runs as
a user other than root and exposes only 8080. Details are in
[Build and CI](../guides/build-and-ci.md).

<!--#endif-->
## Decisions

- [0019 One generated API client](../decisions/0019-one-generated-api-client.md)
<!--#if (localAuth)-->
- [0016 Browser refresh token in a cookie](../decisions/0016-browser-refresh-token-in-a-cookie.md)
<!--#endif-->
- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): one pnpm workspace, Node and pnpm pinned.
